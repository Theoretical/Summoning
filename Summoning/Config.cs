using System;
using System.IO;
using System.Xml.Serialization;

namespace Summoning
{
    [Serializable]
    public class Config
    {
        public string Region;
        public string GamePath;
        public Int32 MaxLevel;
        public Int32 MaxBots;
        public string BotId;
        public string Version;
        public bool UseCfgVersion;
        public bool BuyExpBoost;
        public bool Dominion;
        public bool Dominion3v3;
        public bool CoopVsAI;
        public bool UseDatabase;
        public bool UseBurstLogin;
        public DatabaseConfig Database;

        public static Config Deserialize()
        {
            return (Config)new XmlSerializer(typeof(Config)).Deserialize(File.OpenText("Config.xml"));
        }

        public void Serialize()
        {
            new XmlSerializer(typeof(Config)).Serialize(File.OpenWrite("Config.xml"), this);
        }
    }

    [Serializable]
    public class DatabaseConfig
    {
        public bool UseMySQL;
        public string Host;
        public short Port;
        public string User;
        public string Password;
        public string Database;
        public string Table;
    }
}
