using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using HarmonyLib;
using Mdi.SRRW;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Mdi.SRRW.UI;

internal static class LoadingNativeTests
{
    const BindingFlags I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static SRRWGameSave selected,seen;
    static ManualResetEventSlim entered,release;
    static bool failConstructor,failPrepare,failRoute;
    static int routes,filesystemCalls;
    static FieldInfo pending,saved;
    static Type sessionType;
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(Action<string,Action> test)
    {
        sessionType=typeof(SRBugfix.LoadingFix).GetNestedType("Session",BindingFlags.NonPublic);
        pending=typeof(GameController).GetField("pendingGame",S);saved=typeof(GameController).GetField("lastSave",S);
        var originalPending=pending.GetValue(null);var originalSave=saved.GetValue(null);
        var harmony=new Harmony("tests.loading.native-boundaries");
        try{
            harmony.Patch(typeof(SRRWGame).GetConstructor(new[]{typeof(GameData),typeof(MapMetaData),typeof(SRRWGameSave),typeof(bool)}),prefix:Hook("Construct"));
            harmony.Patch(typeof(GameController).GetMethod("PrepareSaveForLoading",S),prefix:Hook("Prepare"));
            harmony.Patch(typeof(GameController).GetMethod("TransitionToAppropriateScene",I),prefix:Hook("Route"));
            foreach(string name in new[]{"Initialize","GetSavegamePath","GetPlayerProfilePath","ReadAllText","WriteAllText","WriteTextAsync","Delete"})
                harmony.Patch(typeof(PlatformFileSystem).GetMethod(name,S),prefix:Hook("ForbidFilesystem"));
            harmony.Patch(typeof(PlayerProfile).GetMethod("Save",S),prefix:Hook("ForbidFilesystem"));
            test("NativeReconstructionFailureEndsWithErrorInsteadOfWaiting",()=>{
                Reset();failConstructor=true;var f=new Fixture();var sequence=f.Start();Drain(sequence);
                Check(sequence.Error is InvalidDataException&&pending.GetValue(null)==null&&routes==0,"failure did not terminate without publication");
            });
            test("PreparationFailureStartsNoConstructor",()=>{
                Reset();failPrepare=true;var f=new Fixture();var sequence=f.Start();Drain(sequence);
                Check(sequence.Error is InvalidDataException&&!entered.IsSet&&routes==0,"preparation error started reconstruction");
            });
            test("NativeSuccessPublishesOnceOnMainThreadThenRoutes",()=>{
                Reset();int thread=Thread.CurrentThread.ManagedThreadId;var f=new Fixture();var sequence=f.Start();Drain(sequence);
                Check(sequence.Error==null&&pending.GetValue(null)!=null&&routes==1&&routeThread==thread,"success publication/routing failed");
            });
            test("LateSuccessCannotPublishAfterOwnerExit",()=>{
                Reset();release.Reset();var f=new Fixture();var sequence=f.Start();StepUntilEntered(sequence);
                f.Owns=false;Check(!sequence.MoveNext()&&pending.GetValue(null)==null&&routes==0,"owner exit accepted late publication");
                release.Set();WaitWorker(f);
            });
            test("CompetingPendingGameIsPreserved",()=>{
                Reset();release.Reset();var f=new Fixture();var sequence=f.Start();StepUntilEntered(sequence);
                var other=Blank<SRRWGame>();pending.SetValue(null,other);release.Set();Drain(sequence);
                Check(sequence.Error is InvalidOperationException&&ReferenceEquals(pending.GetValue(null),other)&&routes==0,"another load was overwritten");
            });
            test("RoutingFailureWithdrawsOnlyItsPublication",()=>{
                Reset();failRoute=true;var f=new Fixture();var sequence=f.Start();Drain(sequence);
                Check(sequence.Error is InvalidDataException&&pending.GetValue(null)==null,"routing error retained failed pending game");
            });
            test("QueuedRetryUsesItsCapturedSave",()=>{
                Reset();release.Reset();var old=new Fixture();var first=old.Start();StepUntilEntered(first);
                old.Owns=false;Check(!first.MoveNext(),"old owner remained active");
                var intended=Blank<SRRWGameSave>();selected=intended;var retry=new Fixture();var second=retry.Start();
                Check(second.MoveNext(),"retry did not wait for earlier constructor");
                saved.SetValue(null,Blank<SRRWGameSave>());release.Set();WaitWorker(old);Drain(second);
                Check(second.Error==null&&ReferenceEquals(seen,intended)&&routes==1,"queued worker read static lastSave instead of captured save");
            });
            test("RecoveryFixturesNeverAccessSaveProfileOrFilesystem",()=>Check(filesystemCalls==0,"forbidden persistence entry was called"));
        }finally{
            release?.Set();harmony.UnpatchAll(harmony.Id);pending.SetValue(null,originalPending);saved.SetValue(null,originalSave);
        }
    }
    sealed class Fixture
    {
        internal bool Owns=true;internal object Session;
        internal SRBugfix.LoadingSequence Start(){
            var method=sessionType.GetMethod("Reconstruct",I);Check(method!=null,"native reconstruction has no owned result path");
            Session=FormatterServices.GetUninitializedObject(sessionType);
            sessionType.GetField("Game",I).SetValue(Session,Blank<GameController>());
            sessionType.GetField("Flow",I).SetValue(Session,new SRBugfix.LoadingFlow(()=>Owns,()=>{},d=>{},()=>{},()=>{},e=>{},()=>{},()=>true));
            return new SRBugfix.LoadingSequence((IEnumerator)method.Invoke(Session,new object[]{"synthetic-only",null,null,false}));
        }
    }
    static int routeThread;
    static bool Construct(SRRWGameSave save){seen=save;entered.Set();Check(release.Wait(5000),"synthetic constructor gate timed out");if(failConstructor)throw new InvalidDataException("synthetic constructor failure");return false;}
    static bool Prepare(){if(failPrepare)throw new InvalidDataException("synthetic preparation failure");saved.SetValue(null,selected);return false;}
    static bool Route(){routes++;routeThread=Thread.CurrentThread.ManagedThreadId;if(failRoute)throw new InvalidDataException("synthetic route failure");return false;}
    static void ForbidFilesystem(){Interlocked.Increment(ref filesystemCalls);throw new Exception("fixture attempted persistence access");}
    static void Reset(){entered=new ManualResetEventSlim();release=new ManualResetEventSlim(true);selected=Blank<SRRWGameSave>();seen=null;routes=0;failConstructor=failPrepare=failRoute=false;pending.SetValue(null,null);}
    static void Drain(IEnumerator sequence){var end=DateTime.UtcNow.AddSeconds(5);while(sequence.MoveNext()){Check(DateTime.UtcNow<end,"loading wait did not end");Thread.Sleep(1);}}
    static void StepUntilEntered(IEnumerator sequence){var end=DateTime.UtcNow.AddSeconds(5);while(!entered.IsSet){Check(sequence.MoveNext(),"worker failed before entering");Check(DateTime.UtcNow<end,"worker not entered");Thread.Sleep(1);}}
    static void WaitWorker(Fixture fixture){var work=sessionType.GetField("work",I).GetValue(fixture.Session);var task=(System.Threading.Tasks.Task)work.GetType().GetProperty("Completion",I).GetValue(work,null);Check(task.Wait(5000),"worker did not settle");}
    static HarmonyMethod Hook(string name){return new HarmonyMethod(typeof(LoadingNativeTests).GetMethod(name,S));}
    static T Blank<T>(){return (T)FormatterServices.GetUninitializedObject(typeof(T));}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
