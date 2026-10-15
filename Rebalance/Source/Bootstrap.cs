using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SRRebalance
{
    public static class Bootstrap
    {
        public const string DataHash="4c405e4f179f475a67e8561a5163bddb55e7e6340de83bb876ebb1f94c44275d";
        public const string AssemblyHash="a9d49075bab3953b7748b7e91d94829d325580e870297df8ebdc8d43f55427d0";
        public static JObject Read(string gameRoot,string modRoot)
        {
            string path=Path.Combine(modRoot,"rebalance.json");
            if(!File.Exists(path))throw new InvalidDataException("manifest missing: "+path);
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("manifest exceeds size limit");
            JObject result;
            try
            {
                string text=File.ReadAllText(path);var frames=new Stack<HashSet<string>>();
                using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=32})
                    while(reader.Read())
                    {
                        if(reader.TokenType==JsonToken.StartObject)frames.Push(new HashSet<string>());
                        if(reader.TokenType==JsonToken.PropertyName&&!frames.Peek().Add((string)reader.Value))throw new InvalidDataException("manifest duplicate property "+reader.Value);
                        if(reader.TokenType==JsonToken.EndObject)frames.Pop();
                    }
                result=JObject.Parse(text);
            }
            catch(Exception e){throw new InvalidDataException("manifest invalid: "+e.Message,e);}
            var fingerprints=result["fingerprints"] as JObject;
            if(fingerprints==null||(string)fingerprints["data.unity3d"]!=DataHash||(string)fingerprints["Assembly-CSharp.dll"]!=AssemblyHash)
                throw new InvalidDataException("manifest fingerprint mismatch");
            if(HashFile(path)!=BuiltManifest.Sha256)
                throw new InvalidDataException("manifest checksum mismatch; rebuild loader with the intended manifest");
            if((string)result["version"]!="0.3.5"||!(result["changes"] is JArray)||((JArray)result["changes"]).Count!=132||!(result["additions"] is JArray)||((JArray)result["additions"]).Count!=25)throw new InvalidDataException("unsupported manifest scope");
            CheckHash(Path.Combine(gameRoot,"Star Renegades_Data/data.unity3d"),DataHash);
            CheckHash(Path.Combine(gameRoot,"Star Renegades_Data/Managed/Assembly-CSharp.dll"),AssemblyHash);
            return result;
        }
        public static string HashFile(string file)
        {using(var hash=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static void CheckHash(string path,string expected)
        {if(!File.Exists(path)||HashFile(path)!=expected)throw new InvalidDataException("game fingerprint mismatch: "+path);}
    }
}
