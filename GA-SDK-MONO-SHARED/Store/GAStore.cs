﻿using System;
#if MONO
using SqliteConnection = System.Data.SQLite.SQLiteConnection;
using SqliteTransaction = System.Data.SQLite.SQLiteTransaction;
using SqliteCommand = System.Data.SQLite.SQLiteCommand;
using SqliteDataReader = System.Data.SQLite.SQLiteDataReader;
using SqliteException = System.Data.SQLite.SQLiteException;
using SqliteConnectionStringBuilder = System.Data.SQLite.SQLiteConnectionStringBuilder;
#elif WINDOWS_WSA || !UNITY || WINDOWS_UWP
#if WINDOWS_PHONE
using SQLite;
using SqliteConnection = SQLite.SQLiteConnection;
using SqliteCommand = SQLite.SQLiteCommand;
using SqliteException = SQLite.SQLiteException;
#else
using Microsoft.Data.Sqlite;
#endif
using System.Reflection;
#else
#if !UNITY_EDITOR
using Mono.Data.Sqlite;
#endif
#endif
using System.Collections.Generic;
using GameAnalyticsSDK.Net.Utilities;
using GameAnalyticsSDK.Net.Logging;
using GameAnalyticsSDK.Net.Device;
using System.IO;
#if UNITY_SAMSUNGTV
using System.Runtime.InteropServices;
#endif
#if WINDOWS_WSA || WINDOWS_PHONE
using Windows.Storage;
using Windows.Storage.FileProperties;
using System.Threading.Tasks;
#endif

namespace GameAnalyticsSDK.Net.Store
{
	internal class GAStore
	{
        #region Fields and properties

#if UNITY_SAMSUNGTV
        [DllImport("__Internal")]
        private static extern long sqlite3_memory_used();
#endif

#if UNITY_SAMSUNGTV
        public const bool InMemory = true;
        private const long MaxDbSizeBytes = 2097152;
        private const long MaxDbSizeBytesBeforeTrim = 2621440;
#else
        public const bool InMemory = false;
        private const long MaxDbSizeBytes = 6291456;
        private const long MaxDbSizeBytesBeforeTrim = 5242880;
#endif

        private static readonly GAStore _instance = new GAStore();
		private static GAStore Instance
		{
			get
			{
				return _instance;
			}
		}

		// set when calling "ensureDatabase"
		// using a "writablePath" that needs to be set into the C++ component before
		private string dbPath = "";

		#if !UNITY_EDITOR
		// local pointer to database
		private SqliteConnection SqlDatabase
		{
			get;
			set;
		}
		#endif

		private bool DbReady
		{
			get;
			set;
		}

		private bool _tableReady;
		public static bool IsTableReady
		{
			get { return Instance._tableReady; }
			private set { Instance._tableReady = value; }
		}

        public static bool IsDbTooLargeForEvents
        {
            get { return DbSizeBytes > MaxDbSizeBytes; }
        }

#endregion // Fields and properties

		private GAStore()
		{
		}

#region Public methods

		public static JSONArray ExecuteQuerySync(string sql)
		{
			return ExecuteQuerySync(sql, new Dictionary<string, object>());
		}

		public static JSONArray ExecuteQuerySync(string sql, Dictionary<string, object> parameters)
		{
			return ExecuteQuerySync(sql, parameters, false);
		}

		#if !WINDOWS_PHONE
		public static JSONArray ExecuteQuerySync(string sql, Dictionary<string, object> parameters, bool useTransaction)
		{
			// Force transaction if it is an update, insert or delete.
			if (GAUtilities.StringMatch(sql.ToUpperInvariant(), "^(UPDATE|INSERT|DELETE)"))
			{
				useTransaction = true;
			}

			// Get database connection from singelton sharedInstance
			SqliteConnection sqlDatabasePtr = Instance.SqlDatabase;

			// Create mutable array for results
			JSONArray results = new JSONArray();

			SqliteTransaction transaction = null;
			SqliteCommand command = null;

			try
			{
				if (useTransaction)
				{
					transaction = sqlDatabasePtr.BeginTransaction();
				}

				command = sqlDatabasePtr.CreateCommand();

				if (useTransaction)
				{
					command.Transaction = transaction;
				}
				command.CommandText = sql;
				command.Prepare();

				// Bind parameters
				if (parameters.Count != 0)
				{
					foreach(KeyValuePair<string, object> pair in parameters)
					{
						command.Parameters.AddWithValue(pair.Key, pair.Value);
                    }
				}

                using (SqliteDataReader reader = command.ExecuteReader())
				{
					// Loop through results
					while (reader.Read())
					{
                        // get columns count
                        int columnCount = reader.FieldCount;

                        JSONClass row = new JSONClass();
						for (int i = 0; i < columnCount; i++)
						{
							string column = reader.GetName(i);

							if(string.IsNullOrEmpty(column))
							{
								continue;
							}

                            row[column] = reader.GetValue(i).ToString();
                        }
						results.Add(row);
					}
				}

				if (useTransaction)
				{
					transaction.Commit();
				}
			}
			catch (SqliteException e)
			{
				// TODO(nikolaj): Should we do a db validation to see if the db is corrupt here?
				GALogger.E("SQLITE3 ERROR: " + e);
				results = null;

				if (useTransaction)
				{
					if(transaction != null)
					{
						try
						{
							transaction.Rollback();
						}
						catch (SqliteException ex)
						{
							GALogger.E("SQLITE3 ROLLBACK ERROR: " + ex);
						}
						finally
						{
							transaction.Dispose();
						}
					}
				}
			}
			finally
			{
				if(command != null)
				{
					command.Dispose();
				}

				if(transaction != null)
				{
					transaction.Dispose();
				}
			}

			// Return results
			return results;
		}

		#else
		public static JSONArray ExecuteQuerySync(string sql, Dictionary<string, object> parameters, bool useTransaction)
		{
			#if !UNITY_EDITOR
			// Force transaction if it is an update, insert or delete.
			if (GAUtilities.StringMatch(sql.ToUpperInvariant(), "^(UPDATE|INSERT|DELETE)"))
			{
				useTransaction = true;
			}

			// Get database connection from singelton sharedInstance
			SqliteConnection sqlDatabasePtr = Instance.SqlDatabase;

			// Create mutable array for results
			JSONArray results = new JSONArray();

			SqliteCommand command = null;

			try
			{
				if (useTransaction)
				{
					sqlDatabasePtr.BeginTransaction();
				}

				command = sqlDatabasePtr.CreateCommand();

				command.CommandText = sql;

				// Bind parameters
				if (parameters.Count != 0)
				{
					foreach(KeyValuePair<string, object> pair in parameters)
					{
						command.Bind(pair.Key, pair.Value);
                    }
				}

				// Loop through results
				foreach (List<Tuple<string, string>> reader in command.ExecuteQueryMY()) {
                    // get columns count
                    int columnCount = reader.Count;

                    JSONClass row = new JSONClass();
					for (int i = 0; i < columnCount; i++)
					{
						string column = reader[i].Item1;

						if(string.IsNullOrEmpty(column))
						{
							continue;
						}

                        row[column] = reader[i].Item2.ToString();
                    }
					results.Add(row);
				}
				

				if (useTransaction)
				{
					//transaction.Commit();
					sqlDatabasePtr.Commit();
				}
			}
			catch (SqliteException e)
			{
				// TODO(nikolaj): Should we do a db validation to see if the db is corrupt here?
				GALogger.E("SQLITE3 ERROR: " + e);
				results = null;

				
			}
			finally
			{
				
			}

			// Return results
			return results;
			#else
			return null;
			#endif
		}
		#endif

		public static bool EnsureDatabase(bool dropDatabase)
		{
			#if !UNITY_EDITOR
			// lazy creation of db path
			if(string.IsNullOrEmpty(Instance.dbPath))
			{
				// initialize db path
#pragma warning disable 0429
				Instance.dbPath = InMemory ? ":memory:" : Path.Combine(GADevice.WritablePath, "ga.sqlite3");
#pragma warning restore 0429
				GALogger.D("Database path set to: " + Instance.dbPath);
			}

			// Open database
			try
			{
#if UNITY || WINDOWS_PHONE
                Instance.SqlDatabase = new SqliteConnection("URI=file:" + Instance.dbPath + ";Version=3");
#else
                Instance.SqlDatabase = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = Instance.dbPath
                } + "");
#endif
				#if !WINDOWS_PHONE
                Instance.SqlDatabase.Open();
				#endif
				Instance.DbReady = true;
				GALogger.I("Database opened: " + Instance.dbPath);
			}
			catch (Exception e)
			{
				Instance.DbReady = false;
				GALogger.W("Could not open database: " + Instance.dbPath + " " + e);
				return false;
			}

			if (dropDatabase)
			{
				GALogger.D("Drop tables");
				ExecuteQuerySync("DROP TABLE ga_events");
				ExecuteQuerySync("DROP TABLE ga_state");
				ExecuteQuerySync("DROP TABLE ga_session");
				ExecuteQuerySync("DROP TABLE ga_progression");
				ExecuteQuerySync("VACUUM");
			}

			// Create statements
			string sql_ga_events = "CREATE TABLE IF NOT EXISTS ga_events(status CHAR(50) NOT NULL, category CHAR(50) NOT NULL, session_id CHAR(50) NOT NULL, client_ts CHAR(50) NOT NULL, event TEXT NOT NULL);";
			string sql_ga_session = "CREATE TABLE IF NOT EXISTS ga_session(session_id CHAR(50) PRIMARY KEY NOT NULL, timestamp CHAR(50) NOT NULL, event TEXT NOT NULL);";
			string sql_ga_state = "CREATE TABLE IF NOT EXISTS ga_state(key CHAR(255) PRIMARY KEY NOT NULL, value TEXT);";
			string sql_ga_progression = "CREATE TABLE IF NOT EXISTS ga_progression(progression CHAR(255) PRIMARY KEY NOT NULL, tries CHAR(255));";

			JSONArray results = ExecuteQuerySync(sql_ga_events);

			if (results == null)
			{
				return false;
			}

			if (ExecuteQuerySync("SELECT status FROM ga_events LIMIT 0,1") == null)
			{
				GALogger.D("ga_events corrupt, recreating.");
				ExecuteQuerySync("DROP TABLE ga_events");
				results = ExecuteQuerySync(sql_ga_events);
				if (results == null)
				{
					GALogger.W("ga_events corrupt, could not recreate it.");
					return false;
				}
			}

			results = ExecuteQuerySync(sql_ga_session);

			if (results == null)
			{
				return false;
			}

			if (ExecuteQuerySync("SELECT session_id FROM ga_session LIMIT 0,1") == null)
			{
				GALogger.D("ga_session corrupt, recreating.");
				ExecuteQuerySync("DROP TABLE ga_session");
				results = ExecuteQuerySync(sql_ga_session);
				if (results == null)
				{
					GALogger.W("ga_session corrupt, could not recreate it.");
					return false;
				}
			}

			results = ExecuteQuerySync(sql_ga_state);

			if (results == null)
			{
				return false;
			}

			if (ExecuteQuerySync("SELECT key FROM ga_state LIMIT 0,1") == null)
			{
				GALogger.D("ga_state corrupt, recreating.");
				ExecuteQuerySync("DROP TABLE ga_state");
				results = ExecuteQuerySync(sql_ga_state);
				if (results == null)
				{
					GALogger.W("ga_state corrupt, could not recreate it.");
					return false;
				}
			}

			results = ExecuteQuerySync(sql_ga_progression);

			if (results == null)
			{
				return false;
			}

			if (ExecuteQuerySync("SELECT progression FROM ga_progression LIMIT 0,1") == null)
			{
				GALogger.D("ga_progression corrupt, recreating.");
				ExecuteQuerySync("DROP TABLE ga_progression");
				results = ExecuteQuerySync(sql_ga_progression);
				if (results == null)
				{
					GALogger.W("ga_progression corrupt, could not recreate it.");
					return false;
				}
			}

            // All good
            TrimEventTable();

			IsTableReady = true;
			GALogger.D("Database tables ensured present");

			return true;
			#else
			return false;
			#endif
		}

#pragma warning disable 0162
        public static void SetState(string key, string value)
		{
			#if !UNITY_EDITOR
			if (value == null)
			{

                if (InMemory)
                {
#if UNITY
					if(UnityEngine.PlayerPrefs.HasKey(State.GAState.InMemoryPrefix + key))
                    {
                        UnityEngine.PlayerPrefs.DeleteKey(State.GAState.InMemoryPrefix + key);
                    }
#else
					GALogger.W("SetState: No implementation yet for InMemory=true");
#endif
                }
                else
                {
                    Dictionary<string, object> parameterArray = new Dictionary<string, object>();
                    parameterArray.Add("$key", key);
                    ExecuteQuerySync("DELETE FROM ga_state WHERE key = $key;", parameterArray);
                }
            }
			else
			{
                if (InMemory)
                {
#if UNITY
                    UnityEngine.PlayerPrefs.SetString(State.GAState.InMemoryPrefix + key, value);
#else
					GALogger.W("SetState: No implementation yet for InMemory=true");
#endif
                }
                else
                {
                    Dictionary<string, object> parameterArray = new Dictionary<string, object>();
                    parameterArray.Add("$key", key);
                    parameterArray.Add("$value", value);
                    ExecuteQuerySync("INSERT OR REPLACE INTO ga_state (key, value) VALUES($key, $value);", parameterArray, true);
                }
			}
			#endif
		}
#pragma warning restore 0162

        public static long DbSizeBytes
        {
            get
            {
#if WINDOWS_WSA || WINDOWS_PHONE
                Task<StorageFile> fileTask = Task.Run<StorageFile>(async () => await StorageFile.GetFileFromPathAsync(Instance.dbPath));
                StorageFile file = fileTask.GetAwaiter().GetResult();
                Task<BasicProperties> propertiesTask = Task.Run<BasicProperties>(async () => await file.GetBasicPropertiesAsync());
                BasicProperties properties = propertiesTask.GetAwaiter().GetResult();

                return (long)properties.Size;
#elif UNITY_SAMSUNGTV
                long result = 0;
                try
                {
                    result = sqlite3_memory_used();
                }
                catch(Exception)
                {
					GALogger.W("DbSizeBytes: sqlite3_memory_used failed using DbSizeBytes=0");
                }

                return result;
#else
                return InMemory ? 0 : new FileInfo(Instance.dbPath).Length;
#endif
            }
		}

#endregion // Public methods

#region Private methods

        private static void TrimEventTable()
        {
            if (DbSizeBytes > MaxDbSizeBytesBeforeTrim)
            {
                JSONArray resultSessionArray = ExecuteQuerySync("SELECT session_id, Max(client_ts) FROM ga_events GROUP BY session_id ORDER BY client_ts LIMIT 3");

                if(resultSessionArray != null && resultSessionArray.Count > 0)
                {
                    string sessionDeleteString = "";

                    for(int i = 0; i < resultSessionArray.Count; ++i)
                    {
                        sessionDeleteString += resultSessionArray[i].AsString;

                        if(i < resultSessionArray.Count - 1)
                        {
                            sessionDeleteString += ",";
                        }
                    }

                    string deleteOldSessionSql = "DELETE FROM ga_events WHERE session_id IN (\"" + sessionDeleteString + "\");";
                    GALogger.W("Database too large when initializing. Deleting the oldest 3 sessions.");
                    ExecuteQuerySync(deleteOldSessionSql);
                    ExecuteQuerySync("VACUUM");
                }
            }
        }

#endregion
    }
}

