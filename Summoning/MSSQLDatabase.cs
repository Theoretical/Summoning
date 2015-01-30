using Flash;
using Summoning.Bot;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace Summoning
{
    class MSSQLDatabase : IDatabase
    {
        private SqlConnection _sqlConnection;
        private string _sqlConnectionString;

        public MSSQLDatabase()
        {
            try
            {
                var cfg = Globals.Configuration.Database;
                _sqlConnectionString = string.Format(
                    "User ID='{0}';Password='{1}';Server={2};Database={3};Trusted_Connection=False;Connection Timeout = 3;Pooling=True;MultipleActiveResultSets=True",
                    cfg.User, cfg.Password, cfg.Host, cfg.Database);

                _sqlConnection = new SqlConnection(_sqlConnectionString);
                _sqlConnection.Open();
            }
            catch (Exception e)
            {
                Log.Error("Error connection to MSSQL database: {0}", e.Message);
            }
        }

        public bool ContainValidAccounts(int count = 1)
        {
            lock(_sqlConnection)
            {
                using(var command = new SqlCommand("SELECT COUNT(id) as count FROM accounts WHERE finished=0 and progress=0", _sqlConnection))
                {
                    using(var reader = command.ExecuteReader())
                    {
                        if (reader == null || !reader.Read())
                            return false;

                        return Convert.ToInt32(reader["count"]) >= count;
                    }
                }
            }
        }

        public List<Account> FetchInitialAccounts(int length = 6)
        {
            lock(_sqlConnection)
            {
                using(var command = new SqlCommand("dbo.spFetchAccounts", _sqlConnection))
                {
                    command.CommandType = System.Data.CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@count", length);
                    command.Parameters.AddWithValue("@region", Globals.Configuration.Region);
                    command.Parameters.AddWithValue("@botid", Globals.Configuration.BotId);

                    using(var reader = command.ExecuteReader())
                    {
                        var accounts = new List<Account>();

                        while(reader.Read())
                        {
                            var account = new Account();
                            account.Username = Convert.ToString(reader["username"]);
                            account.Password = Convert.ToString(reader["password"]).Replace("\r", "");
                            account.Level = 1;
                            accounts.Add(account);
                        }

                        return accounts;
                    }
                }
            }
        }

        public Account FetchAccount()
        {
            lock (_sqlConnection)
            {
                using (var command = new SqlCommand("dbo.spFetchAccount", _sqlConnection))
                {
                    command.CommandType = System.Data.CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@region", Globals.Configuration.Region);
                    command.Parameters.AddWithValue("@botid", Globals.Configuration.BotId);

                    using (var reader = command.ExecuteReader())
                    {
                        var account = new Account();
                        if (reader == null || !reader.Read())
                            return null;

                        account.Username = Convert.ToString(reader["username"]);
                        account.Password = Convert.ToString(reader["password"]).Replace("\r", "");
                        account.Level = 1;

                        return account;
                    }
                }
            }
        }

        public void SetFinished(Account account)
        {
            lock(_sqlConnection)
            {
                using(var command = new SqlCommand("UPDATE accounts SET finished=1, dateFinished=GETDATE() WHERE username=@user", _sqlConnection))
                {
                    command.Parameters.AddWithValue("@user", account.Username);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SetProgress(Account account, int progress)
        {
            lock (_sqlConnection)
            {
                using (var command = new SqlCommand("UPDATE accounts SET progress=@progress, dateFinished=GETDATE() WHERE username=@user", _sqlConnection))
                {
                    command.Parameters.AddWithValue("@progress", progress);
                    command.Parameters.AddWithValue("@user", account.Username);
                    command.ExecuteNonQuery();
                }
            }
        }

    }
}
