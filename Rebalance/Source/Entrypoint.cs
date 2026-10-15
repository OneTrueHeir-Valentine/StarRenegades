using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
using SRBugfix;

namespace Doorstop
{
    public static class Entrypoint
    {
        const string HookId="local.star-renegades.rebalance";
        static string root,gameRoot;
        static JObject coreManifest,balanceManifest;
        static Action[] installHooks;
        static Action removeHooks;
        static int applications;
        public static void Start()
        {
            root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);gameRoot=Path.GetDirectoryName(root);
            AppDomain.CurrentDomain.AssemblyResolve+=ResolveDependency;
            try{Install();}catch(Exception e){Log("Rebalance not loaded: "+e.Message);}
        }
        static Assembly ResolveDependency(object sender,ResolveEventArgs e)
        {
            string name=new AssemblyName(e.Name).Name;if(name.IndexOfAny(new[]{'/','\\'})>=0)return null;
            string path=Path.Combine(root,name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);
            path=Path.Combine(gameRoot,"Star Renegades_Data/Managed/"+name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Install()
        {
            coreManifest=SRBugfix.Bootstrap.Read(gameRoot,root);
            balanceManifest=SRRebalance.Bootstrap.Read(gameRoot,root);
            if(GameData.Instance!=null)throw new InvalidDataException("Game data is already loaded. Restart the game.");
            var harmony=new Harmony(HookId);
            installHooks=RuntimeFixes.Prepare(harmony,Log);
            removeHooks=()=>RuntimeFixes.Remove(harmony);
            var load=typeof(GameData).GetMethod("LoadData",BindingFlags.Instance|BindingFlags.NonPublic);
            if(load==null)throw new InvalidDataException("Supported game-data loader was not found.");
            var flags=BindingFlags.Static|BindingFlags.NonPublic;
            try{harmony.Patch(load,postfix:new HarmonyMethod(typeof(Entrypoint).GetMethod("AfterDataLoad",flags)));}
            catch{removeHooks();throw;}
            Log("Loader ready, version 0.3.4.");
        }
        static void AfterDataLoad(GameData __instance)
        {
            if(Interlocked.Increment(ref applications)!=1){Log("Repeated data loading is unsupported. Restart the game.");return;}
            try
            {
                var applied=SRRebalance.RebalanceActivation.Apply(__instance,coreManifest,balanceManifest,installHooks,removeHooks);
                RuntimeFixes.SetEnabled(true);
                Log("Applied version 0.3.4: "+applied.ChangedFields+" fields, "+applied.AddedRecords+" added records.");
            }
            catch(Exception e){Log("Rebalance not applied; original data retained: "+e);}
        }
        public static void Log(string text)
        {
            string line=DateTime.UtcNow.ToString("o")+" [SRRebalance] "+text;
            try{File.AppendAllText(Path.Combine(root,"rebalance.log"),line+Environment.NewLine);}catch{}
            Console.WriteLine(line);
        }
    }
}
