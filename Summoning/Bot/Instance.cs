using Flash;
using Flash.Riot;
using Flash.Riot.platform.catalog.champion;
using Flash.Riot.platform.clientfacade;
using Flash.Riot.platform.game;
using Flash.Riot.platform.gameinvite;
using Flash.Riot.platform.statistics;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Timers;
using Complete;
using Flash.Riot.platform.matchmaking;

namespace Summoning.Bot
{
    class Instance
    {
        private Container _container;
        private Account _account;
        private Client _client;
        private bool _master;
        private LoginDataPacket _loginDataPacket;
        private List<ChampionDTO> _champions;
        private ProcessHelper _processHelper = null;
        private GameDTO _currentGame;
        private Timer _lobbyTimer;
        private DateTime _lastAction;
        private string _version;
        private DateTime _lastGameTime;
        private bool _disconnectRequested = false;
        private double _summonerId = 0;
        private double _queueId;
        private LobbyStatus _lobbyStatus;
        private bool _inQueue;
        private bool _accepted;
        private object _queueLock = new object();

        // Used to handle instance handlers.
        private delegate void MessageHandler(object value);
        private Dictionary<Type, MessageHandler> _messageHandlers;
        public DateTime LastAction { get { return _lastAction; } }
        public bool Master { get { return _master; } }
        public Account CurrentAccount { get { return _account; } }
        public bool InGame { get { return _processHelper != null; } }
        public double SummonerId { get { return _summonerId; } }

        public Instance(Container container, Account account, string version, bool isMaster = false)
        {
            _container = container;
            _account = account;
            _master = isMaster;
            _champions = new List<ChampionDTO>();
            _version = version;
            
            _messageHandlers = new Dictionary<Type, MessageHandler>()
            {
                {typeof(LobbyStatus), OnLobbyStatus},
                {typeof(GameDTO), OnGameDTO},
                {typeof(PlayerCredentialsDto), OnGamePlayerCredentials},
                {typeof(EndOfGameStats), OnEndOfGame},
                {typeof(InvitationRequest), OnInvitationRequest}
            };
        }

        public void StopImmediately()
        {
            _disconnectRequested = true;
            if (_client != null)
            {
                _client.SetMessageReceived((s, a) => { });
                _client.SetDisconnectHandler((s, o) => { });
                _client.Disconnect();
            }
        }

        public void CreateClient()
        {
            if (_client != null)
            {
                _client.SetDisconnectHandler((s, o) => { });
                _client.Disconnect();
            }

            _client = new Client(Globals.Region, _version);
            _client.SetMessageReceived((s, a) =>
            {
                try
                {
                    if (_messageHandlers.ContainsKey(a.Body.GetType()))
                    {
                        _lastAction = DateTime.Now;
                        _messageHandlers[a.Body.GetType()](a.Body);
                    }
                }
                catch (RtmpSharp.Net.ClientDisconnectedException)
                {
                }
            });
            
            _client.SetDisconnectHandler((s, a) =>
            {
                if (_disconnectRequested || Completed())
                    return;

                var ex = a as ExceptionalEventArgs;
                Log.Write("[{2}] Disconnected due to exception: {0} | {1}", ex.Exception, ex.Description, _account.Username);
                Start();
            });
        }

        public bool Completed()
        {
            return _account.Level >= Globals.Configuration.MaxLevel;
        }

        public void Quit()
        {
            _disconnectRequested = true;
            _client.Disconnect();
        }

        public async Task<StartStatus> Start()
        {
            _disconnectRequested = false;
            Log.Write("Logging in as: {0} | {1}", _account.Username, _account.Password);
            
            CreateClient();
            var connected = await _client.Connect(_account.Username, _account.Password);

            if (!connected)
                return StartStatus.Failed;
            
            _lastAction = DateTime.Now;

            _loginDataPacket = await _client.GetLoginDataPacketForUser();

            // Setup default values
            if (_account.Level == 1 && _loginDataPacket.AllSummonerData == null)
            {
                var summoner = await _client.CreateDefaultSummoner(_account.Username.Length >= 16 ? _account.Username.Substring(0, 15) : _account.Username);

                while (summoner == null)
                    summoner = await _client.CreateDefaultSummoner(string.Format("NameFail{0}", Program.GenerateString(5)));
            }
            else
            {
                _account.Level = (int)_loginDataPacket.AllSummonerData.SummonerLevel.Level;
            }

            if (_loginDataPacket.AllSummonerData == null || _loginDataPacket.AllSummonerData.Summoner.DisplayEloQuestionaire)
            {
                await _client.ProcessEloQuestionaire();
                await _client.UpdateProfileIcon(1);
                await _client.SaveSeenTutorialFlag();
                _loginDataPacket = await _client.GetLoginDataPacketForUser();
            }

            _summonerId = _loginDataPacket.AllSummonerData.Summoner.SumId;

            if (Completed())
            {
                return StartStatus.Finished;
            }
             
            var champs = await _client.GetAvailableChampions();
            _champions = champs.ToList().FindAll(c => c.FreeToPlay);

            if (Master)
                _container.GenerateGamePair();

            Process();
            return StartStatus.Ok;
        }

        private async Task<bool> CheckForReconnect()
        {
            if (_processHelper != null)
                return true;

            // This is a MUST need fix
            // if a process crashes we reconnect, but we need to reget the loginDataPacket on start.
            _loginDataPacket = await _client.GetLoginDataPacketForUser();

            var reconnectInfo = _loginDataPacket.ReconnectInfo;
            if (reconnectInfo != null && reconnectInfo.playerCredentials != null)
            {
                Log.Write("[{0}] Reconnecting!", _account.Username);
                if (Master)
                    _lastGameTime = DateTime.Now;

                _processHelper = new ProcessHelper();
                _processHelper.Launch(reconnectInfo.playerCredentials);
                return true;
            }

            return false;
        }

        private void BuyBoost(string url)
        {
            if (!Globals.Configuration.BuyExpBoost)
                return;
            try
            {
                var itemPurchaseUrl = Globals.Region.Purchase;
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
                    args["item_id"] = "boosts_2";
                    args["currency_type"] = "rp";
                    args["quantity"] = "1";
                    args["rp"] = "260";
                    args["ip"] = "null";
                    args["duration_type"] = "PURCHASED";
                    args["duration"] = "3";

                    var res = client.UploadValues(itemPurchaseUrl, args);
                    Log.Write("Purchased 3 day xp boost!");
                }
            }
            catch
            { }
        }


        private async void CreateRoom()
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
                config.GameName = _container.GameName;

                if (Globals.Configuration.Dominion)
                    config.GameMode = "ODIN";
                else
                    config.GameMode = "CLASSIC";

                config.GameTypeConfig = 1;
                config.GamePassword = _container.GamePassword;

                _currentGame = await _client.CreatePracticeGame(config);
                Log.Write("[{0}] Master client created game: {1}:{2}", _account.Username, _container.GameName, _container.GamePassword);

                _lobbyTimer = new Timer();
                _lobbyTimer.Elapsed += _lobbyTimer_Elapsed;
                _lobbyTimer.Interval = TimeSpan.FromMinutes(7).TotalMilliseconds;
                _lobbyTimer.Start();
            }
            catch(RtmpSharp.Messaging.InvocationException ex)
            {
                Log.Error("[{0}] {1}", _account.Username, ex);
                _container.GenerateGamePair();
                CreateRoom();
            }
        }

        void _lobbyTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _container.Reset();
            _lobbyTimer.Stop();
            _lobbyTimer = null;
        }

        private async void Process()
        {
            try
            {
                if (_client == null)
                    return;

                if (await CheckForReconnect())
                    return;

                if (Completed())
                {
                    _account.FinishedLeveling(this, null);
                    return;
                }

                if (_account.Level >= 3)
                {
                    _loginDataPacket = await _client.GetLoginDataPacketForUser();
                    var expBoosts = await _client.GetSumonerActiveBoosts();
                    if (_loginDataPacket.RpBalance >= 260 && new DateTime((long)expBoosts.XpBoostEndDate * 10000) < DateTime.Now)
                    {
                        var url = await _client.GetStoreUrl();
                        BuyBoost(url);
                    }
                }

                if (!Globals.Configuration.CoopVsAI)
                {
                    if (Master)
                    {
                        CreateRoom();
                        return;
                    }

                    var practiceGames = await _client.ListAllPracticeGames();
                    var practiceGame = practiceGames.ToList().Find(g => g.Name.Equals(_container.GameName));

                    if (practiceGame != null)
                    {
                        await _client.JoinGame(practiceGame.Id, _container.GamePassword);
                        return;
                    }

                    Process();
                    return;
                }

                if (Master && _lobbyStatus == null)
                {
                    var queues = await _client.GetAvailableQueues();

                    var queue = queues.ToList().Find(q => q.Type == "BOT" && q.NumPlayersPerTeam == 5 && q.GameMode == "CLASSIC");
                    _queueId = queue.Id;
                    Log.Write("Creating lobbhy for queue.");

                    var lobby = await _client.CreateArrangedBotTeamLobby(queue.Id, "MEDIUM");
                    _lobbyStatus = lobby;

                    Log.Write("Lobby created. {0}", lobby.InvitationID);

                    _container.Bots.ForEach((bot) => {
                        if (!bot.Master && bot.SummonerId != 0)
                            _client.Invite(bot.SummonerId);
                    });
                }
                else if (Master && _lobbyStatus != null)
                {
                    _container.Bots.FindAll(b => Array.IndexOf(_lobbyStatus.Members, b.SummonerId) == -1).ForEach((bot) => {
                        if (!bot.Master)
                            _client.Invite(bot.SummonerId);
                    });
                }
            }
            catch (RtmpSharp.Net.ClientDisconnectedException ex)
            {
                if (_disconnectRequested)
                    return;
            }
            catch(RtmpSharp.Messaging.InvocationException ex)
            {
                if (_disconnectRequested)
                    return;

                if (ex.FaultString.Contains("Security"))
                {
                    Quit();
                    Start();
                    return;
                }
                Log.Error("[{0}] join error: {1}", _account.Username, ex.Message);
            }

            Process();
        }

        private void OnLobbyStatus(object lobbyStatus)
        {
            if (_inQueue)
                return;
            var status = lobbyStatus as LobbyStatus;

            var players = Globals.Configuration.Dominion ? 10 : 6;
            if (Globals.Configuration.Dominion3v3)
                players = 6;

            if (Globals.Configuration.MaxBots == 1)
                players = 1;
            if (Globals.Configuration.CoopVsAI)
            {
                _lobbyStatus = status;
                players = 5;
            }

            if (status.Owner.SummonerName == _account.Username && !_inQueue && status.Members.Length != players)
            {
                Log.Write("[{2}] Waiting on bots to join {0}/{1}", status.Members.Length, players, _account.Username);
                return;
            }

            if (status.Members.Length == players && Master && !Globals.Configuration.CoopVsAI && _currentGame != null)
            {
                Log.Write("[{0}] Starting Champion Select! ({1}/{2} players)", _account.Username, players, status.Members.Length);
                _client.StartChampionSelect(_currentGame.id, _currentGame.optimisticLock);
                return;
            }

            lock (_queueLock)
            {
                if (Globals.Configuration.CoopVsAI && status.Members.Length == players && Master && !_inQueue && status.InvitedPlayers.Length == players)
                {
                    var mmp = new MatchMakerParams();
                    mmp.InvitationId = _lobbyStatus.InvitationID;
                    mmp.QueueIds = new Int32[1] { (int)_queueId };
                    mmp.BotDifficulty = "MEDIUM";
                    mmp.Team = new List<int>();

                    status.Members.ForEach(m => mmp.Team.Add((int)m.SummonerId));
                    Log.Write("Attaching to queue");
                    var attachTask = _client.attachTeamToQueue(mmp);
                    _inQueue = true;
                }
            }
                
        }

        public void OnInvitationRequest(object invitationRequest)
        {
            var request = invitationRequest as InvitationRequest;
            _client.Accept(request.InvitationId);
        }

        private void OnGameDTO(object gameDTO)
        {
            var game = gameDTO as GameDTO;

            if (Master)
                _currentGame = game;

            if (game.gameState == "CHAMP_SELECT" && _processHelper == null)
            {
                if (Master)
                    Log.Write("[{0}] Entered Champion select", _account.Username);
                _processHelper = new ProcessHelper();
               // Log.Write("[{0}] Starting champion select.", CurrentAccount.Username);
                _client.SetClientReceivedGameMessage(game.id, "CHAMP_SELECT_CLIENT");

                var champ = Globals.RandomInstance.Next(_champions.Count);
                _client.SelectChampion(_champions[champ].ChampionId);

                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));

                _client.ChampionSelectCompleted();
            }
            else if(game.gameState == "JOINING_CHAMP_SELECT" && !_accepted)
            {
                _client.AcceptPoppedGame(true);
                _accepted = true;
            }
        }

        private void OnGamePlayerCredentials(object playerCred)
        {
            SummoningWebApi.CheckAllowed();
            if (Master)
                Log.Write("[{0}] Launching game.", _account.Username);
            
            var cred = playerCred as PlayerCredentialsDto;
            _processHelper.Launch(cred);
            _lobbyTimer.Stop();
            _lobbyTimer = null;
            _lastGameTime = DateTime.Now;
        }

        private void OnEndOfGame(object endOfGame)
        {
            SummoningWebApi.CheckAllowed();
            if (Master)
            {
                Globals.GamesPlayed++;
                Log.Write("[{0}] Last game length: {1}", _account.Username, DateTime.Now - _lastGameTime);
                Console.Title = string.Format("Summoning - Referrals Of The Future | {0} bots active | Region: {1} | Games Played: {2} | Last Game Length: {3}", Globals.Configuration.MaxBots, Globals.Region.Name, Globals.GamesPlayed, DateTime.Now - _lastGameTime);
            }

            var stats = endOfGame as EndOfGameStats;
            _processHelper.Kill();
            _processHelper = null;
            _lobbyStatus = null;
            _inQueue = false;
            _accepted = false;

            if (stats.LeveledUp)
            {
                _account.Level++;
            }

            Process();

#if !ENTRY
            Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith((t) =>
            {
                var p = System.Diagnostics.Process.GetProcessesByName("League of Legends");

                foreach (var instance in p)
                    instance.Kill();
            });
#endif
        }

    }
}
