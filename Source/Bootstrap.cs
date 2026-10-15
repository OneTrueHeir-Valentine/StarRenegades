using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SRBugfix
{
    public static class Bootstrap
    {
        public const string DataHash="4c405e4f179f475a67e8561a5163bddb55e7e6340de83bb876ebb1f94c44275d";
        public const string AssemblyHash="a9d49075bab3953b7748b7e91d94829d325580e870297df8ebdc8d43f55427d0";
        public static JObject Read(string gameRoot,string modRoot)
        {
            string path=Path.Combine(modRoot,"fixes.json");if(!File.Exists(path))throw new InvalidDataException("fixes.json is missing");
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("patch file is too large");
            JObject result;
            try
            {
                string text=File.ReadAllText(path);var frames=new Stack<HashSet<string>>();
                using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=32})
                    while(reader.Read())
                    {
                        if(reader.TokenType==JsonToken.StartObject)frames.Push(new HashSet<string>());
                        if(reader.TokenType==JsonToken.PropertyName&&!frames.Peek().Add((string)reader.Value))throw new InvalidDataException("duplicate JSON property "+reader.Value);
                        if(reader.TokenType==JsonToken.EndObject)frames.Pop();
                    }
                result=JObject.Parse(text);
            }
            catch(InvalidDataException){throw;}
            catch(Exception e){throw new InvalidDataException("invalid patch JSON: "+e.Message,e);}
            if(HashFile(path)!=BuiltManifest.Sha256)throw new InvalidDataException("patch checksum mismatch");
            var fp=result["fingerprints"] as JObject;
            if(fp==null||(string)fp["data.unity3d"]!=DataHash||(string)fp["Assembly-CSharp.dll"]!=AssemblyHash)throw new InvalidDataException("patch fingerprints mismatch");
            if((string)result["version"]!="0.1.4"||!(result["changes"] is JArray)||((JArray)result["changes"]).Count!=346||!(result["additions"] is JArray)||((JArray)result["additions"]).Count!=36)throw new InvalidDataException("unsupported patch scope");
            if(HashFile(Path.Combine(gameRoot,"Star Renegades_Data/data.unity3d"))!=DataHash||HashFile(Path.Combine(gameRoot,"Star Renegades_Data/Managed/Assembly-CSharp.dll"))!=AssemblyHash)throw new InvalidDataException("game files do not match the supported Steam build");
            return result;
        }
        public static string HashFile(string path){using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    }
}
