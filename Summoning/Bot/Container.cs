using System;
using System.Collections.Generic;
using Flash.Riot.Region;
using System.Threading.Tasks;
using Flash;

namespace Summoning.Bot
{
    enum StartStatus
    {
        Ok,
        Failed,
        Finished
    }

    class Container
    {
        private string _gameName;
        private string _gamePassword;
        public List<Instance> Bots;
        private Random _random;
        private string _version;

        public string GameName { get { return _gameName; } }
        public string GamePassword { get { return _gamePassword; } }
        public string GenerateString()
        {
            _random = new Random();
            var master = Bots.Find(b => b.Master);
            var masterName = master != null ? master.CurrentAccount.Username : Program.GenerateString(5);
            return string.Format("{0}'s game: {1}", masterName, _random.Next(56000));
        }

        public Container()
        {
            Bots = new List<Instance>();
            _random = new Random();     
        }

        private void OnAccountFinished(object sender, EventArgs args)
        {   
            var instance = sender as Instance;

            Log.Write("[{0}] has finished leveling.", instance.CurrentAccount.Username);
            Task.Run(() => Program.SetFinished(instance.CurrentAccount));

            var nextAccount = Program.Accounts.Dequeue();
            Bots.Remove(instance);
            AddAccount(Globals.Region, nextAccount, _version, instance.Master);

            try
            {
                instance.StopImmediately();
            }
            catch (System.Threading.ThreadAbortException) { }
            catch (Exception) { }
        }


        public void AddRange(List<Account> accounts, BaseRegion region, string version)
        {
            _version = version;
            accounts.ForEach((account) =>
            {
                account.FinishedLeveling += OnAccountFinished;
                Bots.Add(new Instance(this, account, version, Bots.Count == 0));
            });
        }

        public async void AddAccount(BaseRegion region, Account account, string version, bool isMaster = false)
        {
            if (Bots.FindAll(b => b.CurrentAccount == account).Count > 0)
            {
                Log.Write("[{0}] account already exists!", account.Username);
                AddAccount(Globals.Region, Program.Accounts.Dequeue(), _version, isMaster);
                return;
            }

            Log.Write("[{0}] Loading...", account.Username);
            var bot = new Instance(this, account, version, isMaster);
            Bots.Add(bot);
            var retValue = StartStatus.Failed;

            try
            {
                for (int i = 0; i < 5; ++i)
                {
                    retValue = await bot.Start();
                    if (retValue == StartStatus.Ok)
                        return;
                    else if (retValue == StartStatus.Finished)
                        break;
                }

                if (retValue == StartStatus.Failed)
                {
                    Bots.Remove(bot);
                    AddAccount(Globals.Region, Program.Accounts.Dequeue(), _version, bot.Master);
                }

                if (retValue == StartStatus.Finished)
                {
                    OnAccountFinished(bot, null);
                }
            }
            catch (Exception e)
            {
                Log.Write("Exception: {0}", e);
                var nextAccount = Program.Accounts.Dequeue();
                Bots.Remove(bot);
                AddAccount(Globals.Region, nextAccount, _version, bot.Master);
            }

        }
        public async Task<bool> ConnectAll()
        {
            var tempFailed = new List<Instance>();
            var tempFinished = new List<Instance>();

            foreach (var bot in Bots.FindAll(b => b.SummonerId == 0))
            {
                try
                {
                    StartStatus retValue = StartStatus.Failed;
                    for (int i = 0; i < 5; ++i)
                    {
                        retValue = await bot.Start();
                        if (retValue == StartStatus.Ok || retValue == StartStatus.Finished)
                            break;
                    }

                    if (retValue == StartStatus.Failed)
                    {
                        Log.Write("Login failed :(");
                        tempFailed.Add(bot);
                    }

                    if (retValue == StartStatus.Finished)
                    {
                        tempFinished.Add(bot);
                    }
                }
                catch (Exception e)
                {
                    Log.Write("Exception: {0}", e);
                    tempFailed.Add(bot);
                }

            }

            Log.Write("Iterating through: {0} finished accounts.", tempFinished.Count);
            tempFinished.ForEach((bot) =>
            {
                OnAccountFinished(bot, null);
            });

            Log.Write("Iterating through: {0} failed accounts.", tempFailed.Count);
            foreach(var bot in tempFailed)
            {
                bool master = bot.Master;
                Bots.Remove(bot);
                bot.StopImmediately();
                Program.DatabaseInstance.SetProgress(bot.CurrentAccount, 0);
                var account = Program.Accounts.Dequeue();
                var instance = new Instance(this, account, _version, master);
                Bots.Add(instance);
            }

            if (tempFailed.Count > 0)
                return await ConnectAll();
            return true;
        }

        public void GenerateGamePair()
        {
            _gameName = GenerateString();
            _gamePassword = GenerateString();
        }

        public void Reset()
        {
            Log.Write("[{0}] Resetting clients.", Bots.Find(b => b.Master).CurrentAccount.Username);
            Bots.ForEach(delegate(Instance instance) {
                instance.Quit();
                instance.Start();
            });
        }
    }
}