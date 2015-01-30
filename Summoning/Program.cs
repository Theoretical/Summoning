using System;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using Summoning.Bot;
using Flash;
using Flash.Riot.Region;

namespace Summoning
{
    // Hacky fix.
    class Globals
    {
        public static Random RandomInstance = new Random();
        public static Config Configuration;
        public static BaseRegion Region;
        public static int GamesPlayed = 0;
        public static string CryptKey = "";

        private static void Cipher(ref byte value, uint key)
        {
            value ^= (byte)~(~(~(value ^ (key >> 0x18)) ^ ~(value ^ ((key >> 0x10) & 0xFF))) ^ ~(~(value ^ ((key >> 0x08) & 0xFF)) ^ ~(value ^ (key & 0xFF))));
        }

        public static void LoadCryptKey()
        {
            using(var reader = new FileStream("key.bin", FileMode.Open))
            {
                var buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);

                var key = BitConverter.ToUInt32(buffer, 0);
                for (int i = 4; i < buffer.Length; ++i)
                    Cipher(ref buffer[i], key);

                CryptKey = System.Text.ASCIIEncoding.ASCII.GetString(buffer, 4, buffer.Length - 6);
            }
        }
    }

    class Program
    {
        public static List<Container> Bots = new List<Container>();
        //public static List<Account> Accounts = new List<Account>();
        public static AccountManagement Accounts = new AccountManagement();
        public static IDatabase DatabaseInstance;
        private static int account_index = 0;
        private static object account_locker = new object();


        static void Main(string[] args)
        {
            Console.Title = "Summoning - Referrals of the Future";

            if (Type.GetType("Mono.Runtime") == null)
                Console.WindowWidth = Console.BufferWidth = 128;

            if (!File.Exists("key.bin"))
                return;

            Globals.Configuration = Config.Deserialize();
            Log.Initialize();
            Globals.LoadCryptKey();
            SummoningWebApi.AttemptAuth();

            Globals.Region = SummoningWebApi.FetchRegion();

            if (Globals.Region == null)
                Globals.Region = BaseRegion.Get(Globals.Configuration.Region);

            // Sanity check.
            var processes = System.Diagnostics.Process.GetProcesses().ToList();
#if !ENTRY
            if (!Directory.Exists(Globals.Configuration.GamePath))
            {
                Log.Error("League of legends could not be found! Please update your game config!");
                Console.ReadLine();
                return;
            }

            var latestVersion = GetLatestVersion();
            var installedVersion = Globals.Configuration.GamePath.Substring(0, Globals.Configuration.GamePath.IndexOf("\\deploy"));
            installedVersion = installedVersion.Substring(installedVersion.LastIndexOf("\\")+1);


            // wow this is hacky.
            if (!latestVersion.Equals(installedVersion))
            {
                Log.Error("League of legends is currently not up to date! Launching lol.launcher.admin.exe");

                var path = Globals.Configuration.GamePath;
                path = path.Substring(0, path.IndexOf("RADS"));
                
                var file = path + "\\lol.launcher.admin.exe";

                var p = new Process();
                p.StartInfo.WorkingDirectory = path + "\\";
                p.StartInfo.FileName = file;
        //        p.Start();

               // Console.ReadLine();
              //  return;
            }
#endif


            try
            {
                foreach (var p in processes)
#if !ENTRY
                    if (p.ProcessName.Contains("League of Legends"))
                        p.Kill();
#else
                    if(p.ProcessName.Contains("entry"))
                        p.Kill();
#endif
            }
            catch { }

            if (Globals.Configuration.UseDatabase)
            {
                if (Globals.Configuration.Database.UseMySQL)
                    DatabaseInstance = new Database();
                else
                    DatabaseInstance = new MSSQLDatabase();

                if (!DatabaseInstance.ContainValidAccounts())
                {
                    Log.Error("No valid accounts are currently avaialble in the database!");
                    //      return;
                }
            }

/*            if (Globals.Configuration.MaxBots % 6 != 0 && (Globals.Configuration.Dominion && Globals.Configuration.MaxBots % 10 != 0) && (
                Globals.Configuration.CoopVsAI && Globals.Configuration.MaxBots % 5 != 0))
            {
                Log.Write("MaxBots must be a multiple of 6 or 10 if using dominion.");
                return;
            }*/

            new BotApi().Start();
            Log.Write("Loading accounts.");
            Accounts.Populate();


            var containerSize = Globals.Configuration.Dominion ? 10 : 6;
            if (Globals.Configuration.Dominion3v3)
                containerSize = 6;

            if (Globals.Configuration.CoopVsAI)
                containerSize = 5;

            if (Globals.Configuration.MaxBots == 1)
                containerSize = 1;

            Log.Write("Container Size: {0}", containerSize);
            Launch(containerSize);
            
            while (true)
            {
                Thread.Sleep(TimeSpan.FromMinutes(1));
                Bots.ForEach((container) =>
                {
                    container.Bots.ForEach((instance) =>
                        {
                            if (DateTime.Now - instance.LastAction >= TimeSpan.FromMinutes(20))
                                Restart();
                        });
                });
                SaveAccounts();
            }
        }

        private static async void Launch(int containerSize)
        {
            var version = Globals.Configuration.UseCfgVersion ? Globals.Configuration.Version : GetVersion();
            lock (account_locker)
            {
                for (var i = 0; i < Globals.Configuration.MaxBots; i += containerSize, account_index += containerSize)
                {
                    var container = new Container();
                    container.AddRange(Accounts.DequeueRange(containerSize), Globals.Region, version);
                    Bots.Add(container);
                }
            }

            foreach(var container in Bots)
            {
                if (!Globals.Configuration.UseBurstLogin)
                    await container.ConnectAll();
                else
                    container.ConnectAll();
            }

            Console.Title = string.Format("Summoning - Referrals Of The Future | {0} bots active | Region: {1} | Games Played: {2}", Bots.Count * containerSize, Globals.Region.Name, Globals.GamesPlayed);
        }

        private static string GetVersion()
        {
            var startup = new ProcessStartInfo("python2.7", "version.py");
            if (Type.GetType("Mono.Runtime") == null)
            {
                startup.FileName = "C:\\python27\\python.exe";
                startup.Arguments = "version.py";
            }

            startup.RedirectStandardOutput = true;
            startup.UseShellExecute = false;

            Process p = new Process();
            p.StartInfo = startup;
            p.Start();

            return p.StandardOutput.ReadToEnd().Replace("\r", "").Replace("\n", "");
        }

        public static string GenerateString(int length = 12)
        {
            var characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(characters, length).Select(s => s[Globals.RandomInstance.Next(s.Length)]).ToArray());
        }

        private static void SaveAccounts()
        {
            try
            {
                var last_used = "last_used.txt"; 
                using (var writer = new StreamWriter(last_used, false))
                {
                    foreach (var container in Bots)
                    {
                        foreach (var bot in container.Bots)
                        {
                            var account = bot.CurrentAccount;
                            writer.WriteLine("{0}|{1}|{2}", account.Username, account.Password, account.Level);
                            writer.Flush();
                        }
                    }

                    writer.Close();
                }
            }
            catch { SaveAccounts();  }
        }

        public static void Restart()
        {
            return;
            Log.Close();
            Process.Start(Environment.CurrentDirectory + "\\Summoning.exe");
            Environment.Exit(0);
        }

        public static void SetFinished(Account account)
        {
            lock (account_locker)
            {
                if (Globals.Configuration.UseDatabase)
                    DatabaseInstance.SetFinished(account);
            }

        }

        public static string GetLatestVersion()
        {
            var version = "";

            try
            {
                using (var wc = new WebClient())
                {
                    wc.Proxy = null;
                    version = wc.DownloadString("http://l3cdn.riotgames.com/releases/live/solutions/lol_game_client_sln/releases/releaselisting");
                }

                return version.Split(new string[] { Environment.NewLine }, StringSplitOptions.None)[0];
            }
            catch { return ""; }
        }
    }
}
