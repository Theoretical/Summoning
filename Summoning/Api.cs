//using BookSleeve;
using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Flash;
using Flash.Riot;

namespace Summoning
{
    class ObjectWrapper
    {
        public string node;
        public object data;
    }

    class Api
    {
        private delegate void ApiCallback(HttpListenerContext context, string[] args);
        private HttpListener _listener;
        private Dictionary<string, ApiCallback> _callbacks;
        private List<Client> _clients;
        private int _clientIndex;
        private object _clientLocker = new object();
        private string _host;
        private int _port;
        //private RedisConnection _redis;

        public Api(string host, int port, List<Client> clients)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(string.Format("http://{0}:{1}/", host, port));
            _listener.Prefixes.Add(string.Format("http://localhost:{0}/", port));

            _clients = clients;
            _clientIndex = 0;
            _host = host;
            _port = port;
          //  _redis = new RedisConnection("127.0.0.1");

//            if (Type.GetType("Mono.Runtime") != null)
  //              _redis.Open();

            _callbacks = new Dictionary<string, ApiCallback>()
            {
                {"name", GetSummonerByName},
                {"game", RetrieveInProgressSpectatorGameInfo},
                {"nodes", GetNodes},
                {"stats", GetAggregatedStats},
                {"store", GetStoreUrl}
            };
        }

        private Client Next()
        {
            lock(_clientLocker)
            {
                if (_clientIndex >= _clients.Count)
                    _clientIndex = 0;

                var c = _clients[_clientIndex++];
                
                if (!c.Ready)
                    return Next();

                return c;
            }
        }

        public async void Start()
        {
            _listener.Start();
            Log.Write("Summoning API has started listening on: {0}:{1}", _host, _port);
            while(true)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch(InvalidOperationException)
                {
                    break;
                }
            }
        }

        public void Stop()
        {
            _listener.Stop();
        }

        private bool CheckCache(string key, out string result)
        {
            result = "";
            if (Type.GetType("Mono.Runtime") == null)
                return false;

            //if (_redis.State != RedisConnectionBase.ConnectionState.Open && _redis.State != RedisConnectionBase.ConnectionState.Opening)
            {
              //  var connection = _redis.Open();
                //connection.Wait();
            }

            //var task = _redis.Strings.GetString(0, key);
            //task.Wait();
            
            //if (task.Result == "" || task.Result == string.Empty || task.Result == null)
                return false;

            //result = task.Result;
            //return true;
        }

        private void AddCache(string key, string value, double expireAt)
        {/*
            if (Type.GetType("Mono.Runtime") == null)
                return;

            if (_redis.State != RedisConnectionBase.ConnectionState.Open && _redis.State != RedisConnectionBase.ConnectionState.Opening)
            {
                var connection = _redis.Open();
                connection.Wait();
            }

            _redis.Strings.Append(0, key, value);
            _redis.Keys.Expire(0, key, (int)expireAt);
            */
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            Log.Write("{0}: {1} - {2} - {3}", context.Request.UserHostName, context.Request.HttpMethod, context.Request.RawUrl, context.Request.RemoteEndPoint.Address.ToString());
            var args = context.Request.RawUrl.Substring(1).Split('/');
            if (_callbacks.ContainsKey(args[0]))
            {
                _callbacks[args[0]](context, args);
            }
        }


        private string ObjectToJSON(Client client, object o)
        {
            // a hack!
            var wrapper = new ObjectWrapper()
            {
                node = client.ClientId,
                data = o
            };

            var jss = new JavaScriptSerializer();
            return jss.Serialize(wrapper);
        }

        private void WriteJSON(HttpListenerContext context, string json)
        {
            try
            {
                var jsonBytes = System.Text.ASCIIEncoding.ASCII.GetBytes(json);
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                context.Response.Headers["Access-Control-Origin"] = "*";
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(jsonBytes, 0, jsonBytes.Length);
                context.Response.Close();
            }
            catch (Exception)
            {
                return;
            }
        }

        private void GetNodes(HttpListenerContext context, string[] args)
        {
            List<Dictionary<string, string>> nodes = new List<Dictionary<string, string>>();

            lock(_clients) 
            {            
                foreach(var c in _clients)
                {
                    nodes.Add(new Dictionary<string, string>()
                    {
                        {"node", c.ClientId},
                        {"uptime", c.Uptime.ToString()},
                        {"serviceable", c.Ready.ToString()}
                    });
                }
            }


            var jss = new JavaScriptSerializer();
            var json =  jss.Serialize(nodes);
            WriteJSON(context, json);
        }

        private async void GetSummonerByName(HttpListenerContext context, string[] args)
        {
            args[1] = args[1].Replace("%20", " ");
            var json = "";
            if (!CheckCache("summoner.name." + args[1], out json))
            {
                var client = Next();
                try
                {
                    var summoner = await client.GetSummonerByName(args[1]);
                    json = ObjectToJSON(client, summoner);
                    AddCache("summoner.name." + args[1], json, TimeSpan.FromMinutes(30).TotalSeconds);
                }
                catch (RtmpSharp.Messaging.InvocationException ex)
                {
                    json = ObjectToJSON(client, new Dictionary<string, object>()
                    {
                        {"code", ex.FaultCode},
                        {"detail", ex.FaultDetail},
                        {"string", ex.FaultString},
                        {"success", false}
                    });
                    AddCache("summoner.game." + args[1], json, TimeSpan.FromMinutes(2).TotalSeconds);
                }
                catch (RtmpSharp.Net.ClientDisconnectedException)
                {
                    Log.Write("Disconnected on session: {0}", client.ClientId);
                    client.Reset();
                    GetSummonerByName(context, args);
                }
            }
            WriteJSON(context, json);
        }

        private async void RetrieveInProgressSpectatorGameInfo(HttpListenerContext context, string[] args)
        {
            args[1] = args[1].Replace("%20", " ");
            var json = "";
            if (!CheckCache("summoner.game." + args[1], out json))
            {
                var client = Next();
                try
                {
                    var game = await client.RetrieveInProgressSpectatorGameInfo(args[1]);
                    json = ObjectToJSON(client, game);
                    AddCache("summoner.game." + args[1], json, TimeSpan.FromMinutes(20).TotalSeconds);
                }
                catch (RtmpSharp.Messaging.InvocationException ex)
                {
                    json = ObjectToJSON(client, new Dictionary<string, object>()
                    {
                        {"code", ex.FaultCode},
                        {"detail", ex.FaultDetail},
                        {"string", ex.FaultString},
                        {"success", false}
                    });
                    AddCache("summoner.game." + args[1], json, TimeSpan.FromMinutes(2).TotalSeconds);
                }
                catch (RtmpSharp.Net.ClientDisconnectedException)
                {
                    Log.Write("Disconnected on session: {0}", client.ClientId);
                    client.Reset();
                    RetrieveInProgressSpectatorGameInfo(context, args);
                }
            }
            WriteJSON(context, json);
        }

        private async void GetAggregatedStats(HttpListenerContext context, string[] args)
        {
            var json = "";
            if (!CheckCache("summoner.stats." + args[1], out json))
            {
                var client = Next();
                try
                {
                    var stats = await client.GetAggregatedStats(Convert.ToDouble(args[1]), "CLASSIC", "4");
                    json = ObjectToJSON(client, stats);
                    AddCache("summoner.stats." + args[1], json, TimeSpan.FromMinutes(20).TotalSeconds);
                }
                catch (RtmpSharp.Messaging.InvocationException ex)
                {
                    json = ObjectToJSON(client, new Dictionary<string, object>()
                    {
                        {"code", ex.FaultCode},
                        {"detail", ex.FaultDetail},
                        {"string", ex.FaultString},
                        {"success", false}
                    });
                    AddCache("summoner.stats." + args[1], json, TimeSpan.FromMinutes(2).TotalSeconds);
                }
                catch (RtmpSharp.Net.ClientDisconnectedException)
                {
                    Log.Write("Disconnected on session: {0}", client.ClientId);
                    client.Reset();
                    GetAggregatedStats(context, args);
                }
            }

            WriteJSON(context, json);
        }

        private async void GetStoreUrl(HttpListenerContext context, string[] args)
        {
            var json = "";
            var client = Next();
            try
            {
                var store = await client.GetStoreUrl();
                json = ObjectToJSON(client, store);
            }
            catch (RtmpSharp.Messaging.InvocationException ex)
            {
                json = ObjectToJSON(client, new Dictionary<string, object>()
                    {
                        {"code", ex.FaultCode},
                        {"detail", ex.FaultDetail},
                        {"string", ex.FaultString},
                        {"success", false}
                    });
            }
            catch (RtmpSharp.Net.ClientDisconnectedException)
            {
                Log.Write("Disconnected on session: {0}", client.ClientId);
                client.Reset();
                GetStoreUrl(context, args);
            }

            WriteJSON(context, json);
        }
    }
}
