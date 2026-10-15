using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Mdi.SRRW;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Mdi.SRRW.UI;

internal static class LoadingDiagnosticTests
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int filesystemCalls;
    internal static void Run(Action<string,Action> test)
    {
        var diagnostic=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostic/LoadingDiagnostic.dll"));
        var entry=diagnostic.GetType("Doorstop.Entrypoint");entry.GetField("module",S).SetValue(null,typeof(SRBugfix.LoadingFix).Assembly);
        var saved=typeof(GameController).GetField("lastSave",S);var original=saved.GetValue(null);
        var guard=new Harmony("tests.loading.diagnostic-io-guard");
        try{
            foreach(string name in new[]{"ReadAllText","WriteAllText","WriteTextAsync","Delete"})guard.Patch(typeof(PlatformFileSystem).GetMethod(name,S),prefix:new HarmonyMethod(typeof(LoadingDiagnosticTests).GetMethod("Forbid",S)));
            guard.Patch(typeof(PlayerProfile).GetMethod("Save",S),prefix:new HarmonyMethod(typeof(LoadingDiagnosticTests).GetMethod("Forbid",S)));
            entry.GetMethod("Install",S).Invoke(null,null);
            test("DiagnosticRejectsMissingCandidateRecovery",()=>{
                SRBugfix.LoadingFix.Enabled=false;bool rejected=false;
                try{typeof(GameController).GetMethod("PrepareSaveForLoading",S).Invoke(null,new object[]{"never-open-this.mdi",false});}catch(TargetInvocationException error){rejected=error.GetBaseException() is InvalidOperationException;}
                Check(rejected,"diagnostic accepted disabled recovery");
            });
            test("DiagnosticUsesSyntheticSaveAndRejectsBeforeNativeReconstruction",()=>{
                SRBugfix.LoadingFix.Enabled=true;
                typeof(GameController).GetMethod("PrepareSaveForLoading",S).Invoke(null,new object[]{"never-open-this.mdi",false});
                var synthetic=(SRRWGameSave)saved.GetValue(null);Check(synthetic!=null&&!ReferenceEquals(synthetic,original),"no isolated save fixture");
                var work=new SRBugfix.LoadingWork<SRRWGame>(()=>new SRRWGame(null,null,synthetic,false));
                Check(work.Completion.Wait(5000),"diagnostic worker did not finish");SRRWGame game;Exception error;
                Check(work.TryTake(out game,out error)&&game==null&&error is InvalidDataException,"diagnostic reached native constructor body");
                Check(filesystemCalls==0,"diagnostic touched save/profile filesystem");
            });
        }finally{
            SRBugfix.LoadingFix.Enabled=false;saved.SetValue(null,original);
            new Harmony("local.star-renegades.loading-diagnostic").UnpatchAll("local.star-renegades.loading-diagnostic");guard.UnpatchAll(guard.Id);
        }
    }
    static void Forbid(){filesystemCalls++;throw new Exception("diagnostic attempted filesystem access");}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
