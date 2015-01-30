using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Flash;

namespace Summoning
{
    class BotApi
    {
        private delegate void ApiCallback(HttpListenerContext context, string[] args);
        private HttpListener _listener;
        private Dictionary<string, ApiCallback> _callbacks;

        public BotApi()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://*:8081/");

            _callbacks = new Dictionary<string, ApiCallback>()
            {
                {"containers", GetContainers},
                {"bot", GetBotStatus},
                {"stopall", StopContainer},
                {"stopbot", StopBot},
                {"reload", ReloadAccounts}
            };
        }

        public async void Start()
        {
            _listener.Start();
            Log.Write("Summoning API has started listening!");

            while (true)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException)
                {
                  //  break;
                }
                catch (InvalidOperationException)
                {
                    //break;
                }
            }
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

        private string ObjectToJSON(object o)
        {
            var jss = new JavaScriptSerializer();
            return jss.Serialize(o);
        }

        private void WriteJSON(HttpListenerContext context, string json)
        {
            try
            {
                var jsonBytes = System.Text.ASCIIEncoding.ASCII.GetBytes(JsonHelper.FormatJson(json));
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


        private void GetContainers(HttpListenerContext context, string[] args)
        {
            Log.Write("wat");
            WriteJSON(context, ObjectToJSON(Program.Bots));
        }

        private void GetBotStatus(HttpListenerContext context, string[] args)
        {

        }

        private void StopContainer(HttpListenerContext context, string[] args)
        {

        }

        private void StopBot(HttpListenerContext context, string[] args)
        {

        }

        private void ReloadAccounts(HttpListenerContext context, string[] args)
        {

        }
    }
}
