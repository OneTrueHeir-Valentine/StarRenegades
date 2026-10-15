// Optional developer diagnostic. Never part of the normal player payload.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Mdi.SRRW.Engine;
using Mdi.SRRW.UI;

namespace Doorstop
{
    public static class Entrypoint
    {
        static string root,modRoot,gameRoot;
        static bool syntheticLoad;
        static Assembly module;
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        public static void Start()
        {
            root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var lines=File.ReadAllLines(Path.Combine(root,"test-paths.txt"));gameRoot=lines[0];modRoot=lines[1];
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
                var name=new AssemblyName(e.Name).Name+".dll";
                var path=Path.Combine(modRoot,name);
                return Assembly.LoadFrom(File.Exists(path)?path:Path.Combine(gameRoot,"Star Renegades_Data/Managed",name));
            };
            module=Assembly.LoadFrom(Path.Combine(modRoot,Path.GetFileName(modRoot)+".dll"));
            module.GetType("Doorstop.Entrypoint").GetMethod("Start",Static).Invoke(null,null);
            Install();
            File.WriteAllText(Path.Combine(root,"diagnostic-started.txt"),File.ReadAllText(Path.Combine(root,"diagnostic-nonce.txt")));
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Install()
        {
            var harmony=new Harmony("local.star-renegades.loading-diagnostic");
            harmony.Patch(typeof(GameController).GetMethod("PrepareSaveForLoading",Static),prefix:new HarmonyMethod(typeof(Entrypoint).GetMethod("Prepare",Static)));
            harmony.Patch(typeof(SRRWGame).GetConstructor(new[]{typeof(Mdi.SRRW.Data.GameData),typeof(Mdi.SRRW.Data.MapMetaData),typeof(SRRWGameSave),typeof(bool)}),prefix:new HarmonyMethod(typeof(Entrypoint).GetMethod("Construct",Static)));
        }
        static bool Prepare()
        {
            if(!(bool)module.GetType("SRBugfix.LoadingFix").GetProperty("Enabled",Static).GetValue(null,null))throw new InvalidOperationException("Loading diagnostic requires the candidate recovery build.");
            // No save file is opened and no native reconstruction body runs.
            typeof(GameController).GetField("lastSave",Static).SetValue(null,FormatterServices.GetUninitializedObject(typeof(SRRWGameSave)));
            syntheticLoad=true;return false;
        }
        static void Construct()
        {
            if(syntheticLoad)throw new InvalidDataException("Loading diagnostic: intentional reconstruction failure. No selected save was read.");
        }
    }
}
