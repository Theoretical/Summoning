using System;
using System.IO;
using System.Net;
using System.Text;
using Flash.Riot.Region;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Management;
using System.Security.Cryptography;
using Flash;

namespace Summoning.Bot
{
    class SummoningWebApi
    {
        private static string GetSHA1(string val)
        {
            var builder = new StringBuilder();

            using(var hash = SHA1.Create())
            {
                var res = hash.ComputeHash(Encoding.UTF8.GetBytes(val));
                res.ForEach(b => builder.Append(b.ToString("x2")));
            }

            return builder.ToString();
        }

        private static string GetHarddrives()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            var outString = "";

            foreach (var drive in searcher.Get())
                outString += drive["SerialNumber"];

            return outString;
        }

        private static string GetCPUName()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (var cpu in searcher.Get())
                return cpu["Name"].ToString();

            return "";
        }

        private static string GetHardwareString()
        {
#if !ENTRY
            var harddrive = GetSHA1(GetHarddrives());
            var cpu = GetSHA1(GetCPUName());
            var key = Globals.CryptKey;

            return string.Format("{0}&cpu={1}&hd={2}", key, cpu, harddrive);
#else
            return string.Format("{0}&cpu=entry&hd=entry", Globals.CryptKey);
#endif
        }

        public static void AttemptAuth()
        {
            return;
            try
            {
                var wr = WebRequest.Create("http://summoning.me/auth");
                var args = Encoding.ASCII.GetBytes(GetHardwareString());
                var jsonSerializer = new JavaScriptSerializer();
                var json = new Dictionary<string, object>();

                wr.Proxy = null;
                wr.Method = "POST";
                wr.ContentType = "application/x-www-form-urlencoded";
                wr.ContentLength = args.Length;
                wr.GetRequestStream().Write(args, 0, args.Length);
                var response = wr.GetResponse();

                using (var reader = new StreamReader(response.GetResponseStream()))
                    if (reader.ReadToEnd() != "Ok")
                        return;
            }
            catch (WebException ex)
            {
                using (var stream = new StreamReader(ex.Response.GetResponseStream()))
                    Log.Error(stream.ReadToEnd());

                Environment.Exit(0);
            }
        }

        public static void CheckAllowed()
        {
            return;
#if ENTRY
            return;
#endif
            try
            {
                var wr = WebRequest.Create("http://summoning.me/allowed");
                var args = Encoding.ASCII.GetBytes(GetHardwareString());
                var jsonSerializer = new JavaScriptSerializer();
                var json = new Dictionary<string, object>();

                wr.Proxy = null;
                wr.Method = "POST";
                wr.ContentType = "application/x-www-form-urlencoded";
                wr.ContentLength = args.Length;
                wr.GetRequestStream().Write(args, 0, args.Length);
                var response = wr.GetResponse();

                using (var reader = new StreamReader(response.GetResponseStream()))
                    if (reader.ReadToEnd() != "Ok")
                        return;
            }
            catch (WebException ex)
            {
                using (var stream = new StreamReader(ex.Response.GetResponseStream()))
                    Log.Error(stream.ReadToEnd());

                Environment.Exit(0);
            }
        }

        public static BaseRegion FetchRegion()
        {
            return null;
            try
            {
                var wr = WebRequest.Create(string.Format("http://summoning.me/region/{0}", Globals.Configuration.Region.ToLower()));
                var args = Encoding.ASCII.GetBytes(Globals.CryptKey);
                var jsonSerializer = new JavaScriptSerializer();
                var json = new Dictionary<string, object>();

                wr.Proxy = null;
                wr.Method = "POST";
                wr.ContentType = "application/x-www-form-urlencoded";
                wr.ContentLength = args.Length;
                wr.GetRequestStream().Write(args, 0, args.Length);

                var response = wr.GetResponse();
                using(var reader = new StreamReader(response.GetResponseStream()))
                    json = jsonSerializer.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());

                response.Close();
                /*
                var region = new BaseRegion();
                
                region.Code = json["code"].ToString();
                region.LoginQueue = json["loginqueue"].ToString();
                region.Name = json["name"].ToString();
                region.Purchase = json["purchase"].ToString();
                region.Server = json["server"].ToString();

                return region;*/
                return null;
            }
            catch(WebException ex)
            {
                if ((int)ex.Status == 401)
                {
                    // lol.
                    Environment.Exit(0);
                }
            }
            return null;
        }
    }
}
