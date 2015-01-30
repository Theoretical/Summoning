using RtmpSharp.Messaging;
using Summoning.Riot;
using Summoning.Riot.platform.catalog.champion;
using Summoning.Riot.platform.game;
using Summoning.Riot.platform.gameinvite;
using Summoning.Riot.platform.statistics;
using Summoning.Riot.Region;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Summoning
{
    enum BotState
    {
        Ready,
        Lobby,
        ChampionSelect,
        Game,
        Exit
    }

    class Bot2
    {
        private Container _contianer;
        private Account _account;
        private Client _client;
        private int _maxLevel;
        private double _lock;
        private double _gameId;
        private Random _random;
        private BotState _state;
        private List<ChampionDTO> _champions = new List<ChampionDTO>();
        private int _processId = -1;
        private bool _master = false;
        private PlayerCredentialsDto _currentGame = null;
        private BaseRegion _region;
        private DateTime _ticks;

        private delegate void MessageHandler(object value);

        private Dictionary<Type, MessageHandler> _messageHandlers;
        
        public BotState State { get { return _state; } }
        public Account BotAccount { get { return _account; } }
        public DateTime Ticks { get { return _ticks; } }

        public RtmpSharp.Net.RtmpClient GetRtmpClient()
        {
            return _client.GetRtmpClient();
        }

        public Bot2(Container container, BaseRegion region, Account account, string version, bool isMaster)
        {
            _contianer = container;
            _region = region;
            _account = account;
            _maxLevel = Globals.Configuration.MaxLevel;
            _master = isMaster;

            _client = new Client(region, version);
            _client.SetMessageReceived(MessageReceivedHandler);
            _random = new Random();
            _state = BotState.Ready;
            _messageHandlers = new Dictionary<Type, MessageHandler>()
            {
                {typeof(LobbyStatus), OnLobbyStatus},
                {typeof(GameDTO), OnGameDTO},
                {typeof(PlayerCredentialsDto), OnGamePlayerCredentials},
                {typeof(EndOfGameStats), OnEndOfGame}
            };
        }


        public void Restart()
        {
            Exit();
            Start();
        }

        public bool IsFinished()
        {
            return _account.CurrentLevel >= _maxLevel;
        }
        
        public void Exit()
        {
            // if we're in a game wait till after the bots finish to terminate them.
            if (_state == BotState.Game)
                _state = BotState.Exit;
            else
                _client.Disconnect();
        }

        public bool IsMaster()
        {
            return _master;
        }
        
        public void SetMaster(bool value)
        {
            _master = value;
        }

        public void GameCrashed()
        {
            if (_state == BotState.Game)
            {
                var process = Process.GetProcessById(_processId);

                if (process != null)
                    process.Kill();

                _state = BotState.Ready;
                _processId = -1;

                StartProcessing();
            }
        }

        private void LaunchGame()
        {
            var p = new Process();

            p.StartInfo.WorkingDirectory = Globals.Configuration.GamePath;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            p.StartInfo.FileName = Path.Combine(Globals.Configuration.GamePath, "League of Legends.exe");
            //p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = "\"8394\" \"LoLLauncher.exe\" \"" + "" + "\" \"" +
                _currentGame.serverIp + " " +
                _currentGame.serverPort + " " +
                _currentGame.encryptionKey + " " +
                _currentGame.summonerId + "\"";
            
            p.Start();

            _state = BotState.Game;
            _processId = p.Id;
            Thread.Sleep(TimeSpan.FromSeconds(10));
        
            Log.Write("[{0}] Injecting module.", _account.Username);
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "Summoning_Injector.exe";
            process.StartInfo.Arguments = p.Id.ToString();
            process.Start();
        }

        private void ProcessWatcher()
        {
            // This is kinda spammy so we need to be careful with it.
            var start = DateTime.Now;

            while(_state == BotState.Game || _state == BotState.Exit)
            {
                var processes = Process.GetProcesses().ToList();
                if (processes.Find(p => p.Id == _processId) == null)
                {
                    Log.Write("[{0}] Game Crashed.", _account.Username);
                    LaunchGame();
                }

                var process = processes.Find(p => p.ProcessName.Contains("BsSndRpt"));
                if (process != null)
                {
                    Log.Write("Found Bugsplat, killing now.");
                    try
                    {
                        process.Kill();
                        if (processes.Find(p => p.Id == _processId) != null)
                        {
                            processes.Find(p => p.Id == _processId).Kill();
                            LaunchGame();
                        }
                    }
                    catch
                    {}
                }

                if ((DateTime.Now - start).Seconds > 5)
                {
                    try
                    {
                        start = DateTime.Now;
                        var player = _client.RetrieveInProgressSpectatorGameInfo(_account.Username);
                        while (!player.IsCompleted)
                            Thread.Sleep(10);

                        var loginPacket = _client.GetLoginDataPacketForUser();

                        while (!loginPacket.IsCompleted)
                            Thread.Sleep(10);

                        if (loginPacket.Result.ReconnectInfo == null || loginPacket.Result.ReconnectInfo.playerCredentials == null)
                        {
                            Log.Write("[{0}] Game has crashed or no game was deceted from login!", _account.Username);
                            _contianer.GameCrashed();
                        }
                    }
                    catch(InvocationException)
                    {
                        // Game Crashed!
                        Log.Write("[{0}] Game has crashed GetSummonerByName!", _account.Username);
                        _contianer.GameCrashed();
                    }
                    catch(Exception)
                    {
                        // Game Crashed!
                        //Log.Write("[{0}] Game has crashed wat!", _account.Username);
                        //_contianer.GameCrashed();
                    }
                }

                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        private void OnLobbyStatus(object lobbyStatus)
        {
            _ticks = DateTime.Now;
            var status = lobbyStatus as LobbyStatus;
            
            var players = Globals.Configuration.Dominion ? 10 : 6;
            if (Globals.Configuration.Dominion3v3)
                players = 6;

            if (status.Owner.SummonerName == _account.Username)
                Log.Write("Waiting on bots to join {0}/{1}....", status.Members.Length, players);

            if (status.Members.Length == players && status.Owner.SummonerName == _account.Username)
            {
                Log.Write("Starting Champion Select!");
                _client.StartChampionSelect(_gameId, _lock);
            }
        }

        private void OnGameDTO(object gameDTO)
        {
            _ticks = DateTime.Now;
            var game = gameDTO as GameDTO;

            if (game.ownerSummary.summonerName == _account.Username)
            {
                _lock = game.optimisticLock;
                _gameId = game.id;
            }

            if (game.gameState == "CHAMP_SELECT")
            {
                if (_state != BotState.ChampionSelect)
                {
                    Log.Write("[{0}] Starting champion select.", BotAccount.Username);
                    _client.SetClientReceivedGameMessage(game.id, "CHAMP_SELECT_CLIENT");

                    var champ = _random.Next(_champions.Count);
                    _client.SelectChampion(_champions[champ].ChampionId);

                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));

                    _client.ChampionSelectCompleted();
                    _state = BotState.ChampionSelect;

                }
            }
        }

        private void OnGamePlayerCredentials(object playerCred)
        {
            if (_state != BotState.Game)
            {
                Log.Write("[{0}] Launching game.", _account.Username);
                _currentGame = playerCred as PlayerCredentialsDto;
                LaunchGame();

                new Thread(new ThreadStart(ProcessWatcher)).Start();
            }
            else
            {
                Log.Write("[{0}] Received player credentials while in-game.", BotAccount.Username);
            }
        }

        private void OnEndOfGame(object endOfGame)
        {
            _ticks = DateTime.Now;
            Process.GetProcessById(_processId).Kill();

            if (_state != BotState.Exit)
            {
                Log.Write("[{0}] has finished their game, restarting.", _account.Username);
                _state = BotState.Ready;
                _processId = -1;

                StartProcessing();
            }
            else
            {
                Log.Write("[{0}] was requested to exit.", _account.Username);
                _client.Disconnect();
                return;
            }
        }

        private void MessageReceivedHandler(object sender, MessageReceivedEventArgs args)
        {
            if (_messageHandlers.ContainsKey(args.Body.GetType()))
                _messageHandlers[args.Body.GetType()](args.Body);
            else
            {
                //Log.Write("[{0}] Unknown type: {1}", BotAccount.Username, args.GetType().Name);
            }
        }

        public async void Start()
        {
            try
            {
                // Connect and login to rtmp
                Log.Write("Logging in as: {0} | {1}", _account.Username, _account.Password);
                await _client.Connect(_account.Username, _account.Password);
                _client.SetMessageReceived(MessageReceivedHandler);

                //Console.Title += " | Summoner: " + _account.Username;
                var loginDataPacket = await _client.GetLoginDataPacketForUser();

                // Setup default values
                if (_account.CurrentLevel == 1 && loginDataPacket.AllSummonerData == null)
                {
                    var summoner = await _client.CreateDefaultSummoner(_account.Username.Length >= 16 ? _account.Username.Substring(0, 15) : _account.Username);
                    await _client.ProcessEloQuestionaire();
                    await _client.UpdateProfileIcon(1);
                    await _client.SaveSeenTutorialFlag();
                }

                var champions = await _client.GetAvailableChampions();

                // To avoid issues later on, we'll just use this..
                _champions = champions.ToList().FindAll(c => c.FreeToPlay);
                _ticks = DateTime.Now;
            }
            catch (InvocationException ex)
            {
                Log.Write("Error on start: {0}|{1}|{2}", ex.FaultString, ex.FaultDetail, ex.FaultCode);
                Start();
            }
            catch(Exception e)
            {
                Log.Write("Error on start non-call: {0}", e);
                Start();
            }

            await StartProcessing();
        }

        private void BuyBoost(string url)
        {
            try
            {
                var itemPurchaseUrl = _region.Purchase;
                var cookies = new CookieContainer();

                var request = (HttpWebRequest)HttpWebRequest.Create(url);
                request.CookieContainer = cookies;
                request.Proxy = null;

                var response = request.GetResponse();
                var body = "";

                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    body = reader.ReadToEnd();
                    reader.Close();
                }

                using (var client = new CookieAwareWebClient())
                {
                    client.CookieContainer = cookies;

                    var args = new NameValueCollection();
                    args["item_id"] = "boosts_6";
                    args["currency_type"] = "rp";
                    args["quantity"] = "1";
                    args["rp"] = "150";
                    args["ip"] = "null";
                    args["duration_type"] = "PURCHASED";
                    args["duration"] = "1";

                    var res = client.UploadValues(itemPurchaseUrl, args);
                    Log.Write("Purchased 3 day xp boost!");
                }
            }catch
            { }
        }

        private async Task<bool> StartProcessing()
        {
            try
            {
                if (_state != BotState.Ready)
                    return false;

                var summoner = await _client.GetSummonerByName(_account.Username.Length >= 16 ? _account.Username.Substring(0, 15) : _account.Username);
                _account.CurrentLevel = (int)summoner.summonerLevel;

                var loginPacket = await _client.GetLoginDataPacketForUser();

                if (loginPacket.RpBalance >= 150 && loginPacket.AllSummonerData.SummonerLevel.Level == 3)
                {
                    var url = await _client.GetStoreUrl();
                    BuyBoost(url);
                }

                if (IsFinished())
                {
                    Log.Write("[{0}] has finished leveling!", _account.Username);
                    return false;
                }


                if (!IsMaster())
                {
                    var gameId = 0.0d;
                    var games = await _client.ListAllPracticeGames();

                    var game = games.ToList().Find(g => g.Name.Equals(_contianer.GameName));
                    gameId = game != null ? game.Id : gameId;

                    if (gameId == 0)
                    {
                        if (loginPacket.ReconnectInfo != null && loginPacket.ReconnectInfo.playerCredentials != null)
                        {
                            Log.Write("[{0}] Reconnecting to game.", _account.Username);
                            _currentGame = loginPacket.ReconnectInfo.playerCredentials;

                            LaunchGame();
                            new Thread(new ThreadStart(ProcessWatcher)).Start();
                            return true;
                        }
                        else
                        {
                            await StartProcessing();
                            return true;
                        }
                    }

                    try
                    {
                        Log.Write("[{0}] Joining game: {1}", _account.Username, gameId);
                        await _client.JoinGame(gameId, _contianer.GamePassword);
                        _state = BotState.Lobby;
                    }
                    catch (InvocationException e)
                    {
                        Log.Write("Error: {0}", e.Message);
                        StartProcessing();
                        return true;
                    }
                    catch (RtmpSharp.Net.ClientDisconnectedException e)
                    {
                        Restart();
                        return true;
                    }
                }
                else
                {
                    try
                    {
                        var config = new PracticeGameConfig();

                        if (Globals.Configuration.Dominion)
                            config.GameMap = GameMap.TheCrystalScar;
                        else
                            config.GameMap = GameMap.TheTwistedTreeline;

                        config.MaxNumPlayers = Globals.Configuration.Dominion ? 10 : 6;
                        if (Globals.Configuration.Dominion3v3)
                            config.MaxNumPlayers = 6;

                        config.AllowSpectators = "ALL";
                        config.GameName = _contianer.GameName;

                        if (Globals.Configuration.Dominion)
                            config.GameMode = "ODIN";
                        else
                            config.GameMode = "CLASSIC";

                        config.GameTypeConfig = 1;
                        config.GamePassword = _contianer.GamePassword;

                        var custom_game = await _client.CreatePracticeGame(config);
                        Log.Write("[{0}] Master client created game: {1}:{2}", _account.Username, _contianer.GameName, _contianer.GamePassword);

                        _lock = custom_game.optimisticLock;
                        _gameId = custom_game.id;
                        _master = true;

                        return true;
                    }
                    catch (RtmpSharp.Messaging.InvocationException)
                    {
                        Log.Write("[{0}] Player is already in a game, attempting to connect.", _account.Username);
                    }
                    catch (RtmpSharp.Net.ClientDisconnectedException e)
                    {
                        Restart();
                        return true;
                    }

                    if (loginPacket.ReconnectInfo.playerCredentials != null)
                    {
                        // neat!
                        _currentGame = loginPacket.ReconnectInfo.playerCredentials;

                        LaunchGame();
                        new Thread(new ThreadStart(ProcessWatcher)).Start();
                    }
                    else
                    {
                        Log.Write("[{0}] No reconnect information found!", _account.Username);
                        Thread.Sleep(TimeSpan.FromSeconds(20));
                        await StartProcessing();
                    }
                }
                return true;
            }
            catch (RtmpSharp.Net.ClientDisconnectedException ex)
            {
                Log.Write("[{0}] Disconnected. Reconnecting.", _account.Username);
                Start();
                return true;
            }
        } 
    }
}
