using System;
using System.Collections.Generic;
using System.IO;
using Flash;
using Summoning.Bot;

namespace Summoning
{
    class AccountManagement
    {
        private LockFreeQueue<Account> _accounts;
        public Int32 Count { get { return _accounts.Count; } }
        public AccountManagement()
        {
            _accounts = new LockFreeQueue<Account>();
        }

        public void Populate()
        {
            PopulateLastUsed();
            Log.Write("Populated {0} accounts from last_used", _accounts.Count);

            if (Globals.Configuration.UseDatabase && Globals.Configuration.MaxBots - _accounts.Count > 0)
                PopulateRange(Globals.Configuration.MaxBots - _accounts.Count);
            else if (!Globals.Configuration.UseDatabase)
                PopulateFile();
        }

        public Account Dequeue()
        {
            if (_accounts.Count == 0)
            {
                if (Globals.Configuration.UseDatabase && !PopulateSingle())
                {
                    Log.Error("Unable to populate an account, maybe your database is out of valid accounts?");
                    return null;
                }
                else if (!Globals.Configuration.UseDatabase)
                {
                    Log.Error("Ran out of accounts to dequeue from.");
                    return null;
                }
            }

            return _accounts.Dequeue();
        }

        public List<Account> DequeueRange(int range)
        {
            var list = new List<Account>();

            for (var i = 0; i < range; ++i)
                list.Add(Dequeue());

            return list;
        }

        public bool PopulateSingle()
        {
            // this should only be used by the database setup.
            var max_tries = 5;

            for (var i = 0; i < max_tries; ++i)
            {
                var account = Program.DatabaseInstance.FetchAccount();
                if (account != null)
                {
                    _accounts.Enqueue(account);
                    return true;
                }
            }

            return false;
        }

        private void PopulateLastUsed()
        {
            if (!File.Exists("last_used.txt"))
            {
                File.Create("last_used.txt").Close();
                return;
            }

            using (var reader = new StreamReader("last_used.txt"))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var line_args = line.Split('|');

                    if (line_args.Length < 2)
                        continue;

                    var user = line_args[0];
                    var pass = line_args[1];
                    var level = line_args.Length > 2 ? int.Parse(line_args[2]) : 1;

                    var acc = new Account();
                    acc.Username = user;
                    acc.Password = pass;
                    acc.Level = level;

                    _accounts.Enqueue(acc);
                }
            }
        }
        private void PopulateRange(int count)
        {
            var toPopulate = Program.DatabaseInstance.FetchInitialAccounts(count);

            toPopulate.ForEach((account) =>
            {
                _accounts.Enqueue(account);
            });

            if (_accounts.Count < Globals.Configuration.MaxBots)
            {
                Log.Error("Expected: {0} accounts only found {1}", Globals.Configuration.MaxBots, _accounts.Count);
                Globals.Configuration.MaxBots = (_accounts.Count / 10) * 10;
                Log.Error("Coninuning with {0} max bots.", Globals.Configuration.MaxBots);
            }

            Log.Write("Populated {0} accounts from database.", toPopulate.Count);
        }
        private void PopulateFile()
        {
            var accounts_file = "accounts_in_progress.txt";
            var total_before = _accounts.Count;

            if (File.Exists(accounts_file))
            {
                using (var reader = new StreamReader(accounts_file))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var line_args = line.Split('|');
                        var account = new Account();

                        account.Username = line_args[0];
                        account.Password = line_args[1];
                        account.Level = line_args.Length > 2 ? int.Parse(line_args[2]) : 1;
                        _accounts.Enqueue(account);
                    }
                }
            }

            Log.Write("Populated {0} accounts from file", _accounts.Count - total_before);
        }
    }

}
