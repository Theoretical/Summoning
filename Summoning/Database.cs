using Flash;
using MySql.Data.MySqlClient;
using Summoning.Bot;
using System;
using System.Collections.Generic;
using System.Timers;

namespace Summoning
{
    class Database : IDatabase
    {
        private MySqlConnection _connection;
        private string _connectionString;
        private Timer _killTimer;

        public Database()
        {
            _connectionString = string.Format(
                "Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4}; default command timeout=60;",
                Globals.Configuration.Database.Host, Globals.Configuration.Database.Port, Globals.Configuration.Database.Database,
                Globals.Configuration.Database.User, Globals.Configuration.Database.Password);

            _killTimer = new Timer();
            _killTimer.Interval += 15000;
            _killTimer.Elapsed += (s, o) =>
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand("SHOW PROCESSLIST", connection))
                    {
                        using(var reader = cmd.ExecuteReader())
                        {
                            var processIds = new List<int>();
                            while(reader.Read())
                            {
                                if (reader.IsDBNull(7) || reader["info"] == "NULL")
                                    processIds.Add(Convert.ToInt32(reader["id"]));
                            }

                            reader.Close();

                            foreach(var pid in processIds)
                            {
                                using (var killCmd = new MySqlCommand("KILL " + pid, connection))
                                    killCmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
            };
            _killTimer.Start();
        }

        public bool ContainValidAccounts(int count = 1)
        {
            try
            {

                using (var connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand(
                        string.Format("SELECT COUNT(username) as count FROM {0} WHERE progress = 0 and finished = 0", Globals.Configuration.Database.Table), connection))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader == null || !reader.Read())
                            {
                                reader.Close();
                                return false;
                            }

                            return Convert.ToInt32(reader["count"]) >= count;
                        }
                    }
                }
            }
            catch (MySqlException e)
            {
                Log.Error(e.ToString());
            }
            return ContainValidAccounts();
        }

        public List<Account> FetchInitialAccounts(int length = 6)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    using (var lockCmd = new MySqlCommand(string.Format("LOCK TABLES {0} WRITE", Globals.Configuration.Database.Table), connection))
                    {
                        lockCmd.Connection.Open();
                        lockCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new MySqlCommand(
                        string.Format("SELECT username, password FROM {0} WHERE finished = 0 and progress = 0 and LENGTH(username) > 0 order by datecreated ASC limit {1}", Globals.Configuration.Database.Table, length), connection))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            var accounts = new List<Account>();

                            if (reader == null)
                            {
                                Log.Error("Failed to read accounts from database");
                                reader.Close();
                                using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                                {
                                    unlockCmd.ExecuteNonQuery();
                                }

                                cmd.Connection.Close();
                                cmd.Connection.Dispose();
                                return null;
                            }

                            while (reader.Read())
                            {
                                var account = new Account();

                                account.Username = Convert.ToString(reader["username"]);
                                account.Password = Convert.ToString(reader["password"]);
                                account.Level = 1;

                                Log.Write("Appending account list with: {0}", account.Username);
                                accounts.Add(account);
                            }

                            reader.Close();

                            foreach (var account in accounts)
                            {
                                using (var updateCmd = new MySqlCommand(string.Format(
    "UPDATE {0} SET progress=@progress,botid=@botid,DateStarted=NOW() WHERE username=@username", Globals.Configuration.Database.Table), connection))
                                {
                                    updateCmd.Parameters.AddWithValue("@progress", 1);
                                    updateCmd.Parameters.AddWithValue("@username", account.Username);
                                    updateCmd.Parameters.AddWithValue("@botid", Globals.Configuration.BotId);
                                    updateCmd.ExecuteNonQuery();
                                }
                            }


                            using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                            {
                                unlockCmd.ExecuteNonQuery();
                            }

                            cmd.Connection.Close();
                            cmd.Connection.Dispose();
                            return accounts;
                        }
                    }
                }
                catch (MySqlException e)
                {
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                        {
                            unlockCmd.ExecuteNonQuery();
                        }
                    }


                    Log.Error(e.ToString());
                    return FetchInitialAccounts(length);
                }
                finally
                {
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                        {
                            unlockCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public Account FetchAccount()
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    using (var lockCmd = new MySqlCommand(string.Format("LOCK TABLES {0} WRITE", Globals.Configuration.Database.Table), connection))
                    {
                        lockCmd.Connection.Open();
                        lockCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new MySqlCommand(
                        /*
                         * Bug 2: Usernames that were null were being picked.
                         */
                        string.Format("SELECT username, password FROM {0} WHERE finished = 0 and progress = 0 and LENGTH(username) > 0 order by datecreated ASC limit 1", Globals.Configuration.Database.Table), connection))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader == null || !reader.Read())
                            {
                                reader.Close();

                                using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                                {
                                    unlockCmd.ExecuteNonQuery();
                                }


                                cmd.Connection.Close();
                                cmd.Connection.Dispose();
                                return null;
                            }


                            var account = new Account();

                            account.Username = Convert.ToString(reader["username"]);
                            account.Password = Convert.ToString(reader["password"]);
                            account.Level = 1;

                            reader.Close();

                            using (var updateCmd = new MySqlCommand(string.Format(
        "UPDATE {0} SET progress=@progress,botid=@botid,DateStarted=NOW() WHERE username=@username", Globals.Configuration.Database.Table), connection))
                            {
                                updateCmd.Parameters.AddWithValue("@progress", 1);
                                updateCmd.Parameters.AddWithValue("@username", account.Username);
                                updateCmd.Parameters.AddWithValue("@botid", Globals.Configuration.BotId);
                                updateCmd.ExecuteNonQuery();
                            }

                            using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                            {
                                unlockCmd.ExecuteNonQuery();
                            }

                            cmd.Connection.Close();
                            cmd.Connection.Dispose();

                            return account;
                        }
                    }
                }
                catch (MySqlException e)
                {
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                        {
                            unlockCmd.ExecuteNonQuery();
                        }
                    }


                    Log.Error(e.ToString());
                    return FetchAccount();
                }
                finally
                {
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        using (var unlockCmd = new MySqlCommand("UNLOCK TABLES", connection))
                        {
                            unlockCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public void SetProgress(Account account, int progress)
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    using (var cmd = new MySqlCommand(string.Format(
                        "UPDATE {0} SET progress=@progress, WHERE username=@username", Globals.Configuration.Database.Table), connection))
                    {
                        cmd.Connection.Open();
                        cmd.Parameters.AddWithValue("@progress", progress);
                        cmd.Parameters.AddWithValue("@username", account.Username);
                        cmd.ExecuteNonQuery();
                        cmd.Connection.Close();
                        cmd.Connection.Dispose();
                    }
                }
            }
            catch (MySqlException e)
            {
                Log.Error(e.ToString());
                SetProgress(account, progress);
            }
        }

        public void SetFinished(Account account)
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    using (var cmd = new MySqlCommand(string.Format("UPDATE {0} SET finished=1, DateFinished=NOW(),progress=0 WHERE username=@username", Globals.Configuration.Database.Table), connection))
                    {
                        cmd.Connection.Open();
                        cmd.Parameters.AddWithValue("@username", account.Username);
                        cmd.ExecuteNonQuery();
                        cmd.Connection.Close();
                        cmd.Connection.Dispose();
                    }
                }
            }
            catch (MySqlException e)
            {
                Log.Error(e.ToString());
                SetFinished(account);
            }
        }

        public void SetLevel(string username, int level)
        {
            return;
#if PUBLIC
            try
            {
                using(var connection = new MySqlConnection(_connectionString))
                {
                    using(var cmd = new MySqlCommand(string.Format("UPDATE {0} SET level=@level WHERE username=@username", Globals.Configuration.MySQL.Table), connection))
                    {
                        cmd.Connection.Open();
                        cmd.Parameters.AddWithValue("@level", level);
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.ExecuteNonQuery();
                        cmd.Connection.Close();
                    }
                }
            }
            catch(MySqlException e)
            {
                Log.Error(e.ToString());
                SetLevel(username, level);
            }
#endif
        }


        public void SetLastGameTime(string username)
        {
            return;
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    using (var cmd = new MySqlCommand(string.Format("UPDATE {0} SET lastGameTime=NOW() WHERE username=@username", Globals.Configuration.Database.Table), connection))
                    {
                        cmd.Connection.Open();
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.ExecuteNonQuery();
                        cmd.Connection.Close();
                        cmd.Connection.Dispose();
                    }
                }
            }
            catch (MySqlException e)
            {
                Log.Error(e.ToString());
                SetLastGameTime(username);
            }
        }
        public void SetFailed(string username)
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    using (var cmd = new MySqlCommand(string.Format("UPDATE {0} SET finished=1, DateFinished=NOW(),botid=@botid WHERE username=@username", Globals.Configuration.Database.Table), connection))
                    {
                        cmd.Connection.Open();
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@botid", String.Format("Failed-{0}", Globals.Configuration.BotId));
                        cmd.ExecuteNonQuery();
                        cmd.Connection.Close();
                        cmd.Connection.Dispose();
                    }
                }
            }
            catch (MySqlException e)
            {
                Log.Error(e.ToString());
            }
        }
    }
}
