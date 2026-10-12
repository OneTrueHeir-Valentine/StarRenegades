using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Reflection.Emit;
using System.Linq;
using Mdi.SRRW;
using Mdi.SRRW.Data;
using Mdi.SRRW.Data.Achievements;
using SRBugfix;
using HarmonyLib;

internal static class AchievementTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int passed,failed;
    static int Main(string[] args)
    {
        string managed=Path.Combine(args[0],"Star Renegades_Data/Managed");
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(managed,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        UnityEngine.Debug.unityLogger.logEnabled=false;
        Test("SupportedNativeHookShapesResolveWithoutSteamCalls",()=>{
            var targets=AchievementFix.ResolveTargets();
            string[] names={"Initialize","Record","UpdateAchievement","Update","OnUserStatsReceived","OnUserStatsStored","OnEnable","OnDisable"};
            Equal(targets.Length,names.Length,"missing lifecycle target");for(int i=0;i<names.Length;i++)Equal(targets[i].Name,names[i],"wrong hook target");
        });
        Test("NativeOfflineCrossingReconcilesWhenReady",()=>{
            var listener=new Listener();var p=Profile(9,listener);var q=new EarnedAchievementQueue();var a=new Adapter{IsReady=false};
            p.RecordManufacturerKill();q.Reconcile(AchievementFix.ReadEarned(p));q.Tick(a,0);Equal(a.SetIds.Count,0,"submitted offline");
            a.IsReady=true;q.Tick(a,1);Equal(a.SetIds.Count,1,"offline threshold was lost");Equal(a.SetIds[0],"srAchvBeatWar10","wrong Steam ID");Equal(Counter(p),10,"changed local counter");
        });
        Test("NativeLoadedTwentyReplaysOnlyTen",()=>{
            var p=Profile(20,new Listener());var q=new EarnedAchievementQueue();var a=new Adapter();q.Reconcile(AchievementFix.ReadEarned(p));q.Tick(a,0);
            Equal(a.SetIds.Count,1,"loaded earned threshold missing");Equal(a.SetIds[0],"srAchvBeatWar10","awarded unearned threshold");p.RecordManufacturerKill();q.Reconcile(AchievementFix.ReadEarned(p));q.Tick(a,20);Equal(a.SetIds.Count,1,"duplicate submission");Equal(Counter(p),21,"native increment changed");
        });
        Test("NativeFutureThresholdRemainsOrdinary",()=>{
            var listener=new Listener();var p=Profile(24,listener);p.RecordManufacturerKill();Equal(listener.TwentyFive,1,"native future threshold missing");
            var q=new EarnedAchievementQueue();var a=new Adapter();q.Reconcile(AchievementFix.ReadEarned(p));q.Tick(a,0);Equal(a.SetIds.Count,2,"earned thresholds missing");if(a.SetIds.Contains("srAchvBeatWar50"))throw new Exception("unearned 50 awarded");Equal(Counter(p),25,"counter changed");
        });
        Test("BelowThresholdNeverQueues",()=>{
            var p=Profile(8,new Listener());p.RecordManufacturerKill();var q=new EarnedAchievementQueue();var a=new Adapter();q.Reconcile(AchievementFix.ReadEarned(p));q.Tick(a,0);Equal(a.SetIds.Count,0,"unearned submission");
        });
        Test("FailedSetRetriesWithSpacingAndBound",()=>{
            var q=Earned();var a=new Adapter{SetResult=false};q.Tick(a,0);q.Tick(a,1);Equal(a.SetIds.Count,1,"busy retry");q.Tick(a,10);q.Tick(a,20);q.Tick(a,100);Equal(a.SetIds.Count,3,"retry budget incorrect");Equal(a.StoreRequests,0,"stored rejected SetAchievement");
        });
        Test("SuccessfulSetWaitsForStoreAcknowledgment",()=>{
            var q=Earned();var a=new Adapter();q.Tick(a,0);q.Tick(a,10);Equal(a.SetIds.Count,1,"set duplicated while store pending");Equal(a.StoreRequests,1,"store not requested");q.Stored(true,10);q.Tick(a,100);Equal(a.SetIds.Count,1,"confirmed unlock duplicated");
        });
        Test("FailedStoreReissuesSetWithinSameBound",()=>{
            var q=Earned();var a=new Adapter();q.Tick(a,0);q.Stored(false,1);q.Tick(a,2);Equal(a.SetIds.Count,1,"immediate retry on callback");q.Tick(a,11);Equal(a.SetIds.Count,2,"failed store did not resubmit");q.Stored(false,12);q.Tick(a,22);q.Stored(false,23);q.Tick(a,100);Equal(a.SetIds.Count,3,"store callback bypassed budget");
        });
        Test("AlreadyOwnedIsDeduplicated",()=>{
            var q=Earned();var a=new Adapter{Owned=true};q.Tick(a,0);q.Tick(a,10);Equal(a.SetIds.Count,0,"already owned was set again");Equal(a.StoreRequests,0,"already owned store unnecessary");
        });
        Test("FailedQueryNeverBlindlyAwards",()=>{
            var q=Earned();var a=new Adapter{QueryResult=false};q.Tick(a,0);q.Tick(a,10);Equal(a.SetIds.Count,0,"award without valid query");a.QueryResult=true;q.Tick(a,20);Equal(a.SetIds.Count,1,"query failure did not recover");
        });
        Test("InvalidStatsOrDisabledAdapterDoesNotSubmit",()=>{
            var q=Earned();var a=new Adapter{IsReady=false};for(int n=0;n<100;n++)q.Tick(a,n);Equal(a.SetIds.Count,0,"not ready submission");a.IsReady=true;q.Tick(a,100);Equal(a.SetIds.Count,1,"readiness consumed budget");
        });
        Test("SwitchedProfileDropsStaleEarnedQueue",()=>{
            var q=Earned();var a=new Adapter{IsReady=false};q.Tick(a,0);q.Reconcile(new Dictionary<string,string>());a.IsReady=true;q.Tick(a,10);Equal(a.SetIds.Count,0,"stale profile awarded");
        });
        Test("MissingOrAlteredNativeThresholdFailsClosed",()=>{
            var p=Profile(20,new Listener());p.manufacturerKillThresholds["killManufacturers10"]=1;var q=new EarnedAchievementQueue();var a=new Adapter();q.Reconcile(AchievementFix.ReadEarned(p));q.Tick(a,0);Equal(a.SetIds.Count,0,"altered threshold awarded");
        });
        Test("NativeHooksReadinessCallbacksAndDisableGate",()=>{
            using(var fixture=new HookFixture())
            {
                var stats=fixture.Stats;var p=Profile(9,stats);FakeSteam.Initialized=false;p.RecordManufacturerKill();
                AchievementFix.UpdatePostfix(stats);Equal(FakeSteam.SetIds.Count,0,"Steam uninitialized submission");
                FakeSteam.Initialized=true;AchievementFix.UpdatePostfix(stats);Equal(FakeSteam.SetIds.Count,0,"invalid stats submission");
                Set(stats,"areStatsValid",true);AchievementFix.ReceivedPostfix(stats,fixture.Callback("UserStatsReceived_t",2,1));Equal(FakeSteam.SetIds.Count,0,"foreign callback submitted");
                AchievementFix.ReceivedPostfix(stats,fixture.Callback("UserStatsReceived_t",1,2));Equal(FakeSteam.SetIds.Count,0,"failed callback submitted");
                AchievementFix.DisablePostfix(stats);AchievementFix.ReceivedPostfix(stats,fixture.Callback("UserStatsReceived_t",1,1));Equal(FakeSteam.SetIds.Count,0,"disabled callback submitted");
                AchievementFix.EnablePostfix(stats);AchievementFix.ReceivedPostfix(stats,fixture.Callback("UserStatsReceived_t",1,1));Equal(FakeSteam.SetIds.Count,1,"valid callback lost earned counter");
                Equal((bool)Get(stats,"shouldStoreStats"),true,"accepted set did not schedule vanilla store");Equal(Counter(p),10,"hooks changed counter");
            }
        });
        Test("NativeHooksProfileSwitchReplacesPendingCounters",()=>{
            using(var fixture=new HookFixture())
            {
                var old=Profile(20,fixture.Stats);var current=Profile(8,fixture.Stats);old.RecordManufacturerKill();FakeSteam.Initialized=true;Set(fixture.Stats,"areStatsValid",true);
                AchievementFix.UpdatePostfix(fixture.Stats);Equal(FakeSteam.SetIds.Count,0,"old profile still queued");
                current.RecordManufacturerKill();AchievementFix.UpdatePostfix(fixture.Stats);Equal(FakeSteam.SetIds.Count,0,"new profile below threshold awarded");
                current.RecordManufacturerKill();AchievementFix.UpdatePostfix(fixture.Stats);Equal(FakeSteam.SetIds.Count,1,"new profile crossing lost");Equal(Counter(old),21,"old native increment changed");
            }
        });
        Test("NativeStoreFalseKeepsPendingSetAndStoreFlag",()=>{
            using(var fixture=new HookFixture())
            {
                Profile(20,fixture.Stats);FakeSteam.Initialized=true;Set(fixture.Stats,"areStatsValid",true);fixture.Update();
                Equal(FakeSteam.SetIds.Count,1,"earned set missing");FakeSteam.StoreAccepted=false;fixture.Update();fixture.Update();
                Equal(FakeSteam.SetIds.Count,1,"StoreStats false reissued accepted set");Equal(FakeSteam.StoreCalls,2,"native StoreStats false stopped retrying");Equal((bool)Get(fixture.Stats,"shouldStoreStats"),true,"native retry flag lost");
            }
        });
        Test("OlderStoreCallbackCannotConfirmNewProfileBatch",()=>{
            using(var fixture=new HookFixture())
            {
                Profile(20,fixture.Stats);FakeSteam.Initialized=true;Set(fixture.Stats,"areStatsValid",true);fixture.Update();fixture.Update();
                Profile(25,fixture.Stats);fixture.Update();fixture.Update();Equal(FakeSteam.SetIds.Count,3,"new profile thresholds missing");
                AchievementFix.StoredPostfix(fixture.Stats,fixture.Callback("UserStatsStored_t",1,1));
                AchievementFix.StoredPostfix(fixture.Stats,fixture.Callback("UserStatsStored_t",1,8));
                // A fresh readiness session reissues the new profile's rejected requests.
                AchievementFix.DisablePostfix(fixture.Stats);AchievementFix.EnablePostfix(fixture.Stats);fixture.Update();
                Equal(FakeSteam.SetIds.Count,5,"old callback confirmed newly queued achievements");
            }
        });
        Test("ExhaustedAttemptsRecoverOnlyOnNewReadySession",()=>{
            var q=Earned();var a=new Adapter{SetResult=false};q.Tick(a,0);q.Tick(a,10);q.Tick(a,20);q.Tick(a,100);Equal(a.SetIds.Count,3,"unbounded attempts");
            a.SetResult=true;q.Tick(a,200);Equal(a.SetIds.Count,3,"ordinary update reset budget");q.NewReadySession(200);q.Tick(a,200);Equal(a.SetIds.Count,4,"fresh session did not recover");
        });
        Test("MissingStoreCallbackRetriesOnNewReadySession",()=>{
            var q=Earned();var a=new Adapter();q.Tick(a,0);q.Tick(a,100);Equal(a.SetIds.Count,1,"store pending retry spam");q.NewReadySession(100);q.Tick(a,100);Equal(a.SetIds.Count,2,"pending store lost forever");
        });
        Test("AbsentCallbacksSuspendAmbiguousContextAcrossReenable",()=>{
            using(var fixture=new HookFixture())
            {
                var p=Profile(8,fixture.Stats);FakeSteam.Initialized=true;Set(fixture.Stats,"areStatsValid",true);
                for(int n=0;n<100;n++){Set(fixture.Stats,"shouldStoreStats",true);fixture.Update();}
                p.RecordManufacturerKill();p.RecordManufacturerKill();fixture.Update();Equal(FakeSteam.SetIds.Count,0,"ambiguous missing callbacks did not suspend submissions");
                AchievementFix.DisablePostfix(fixture.Stats);AchievementFix.EnablePostfix(fixture.Stats);fixture.Update();Equal(FakeSteam.SetIds.Count,0,"ambiguous ownership revived on same stats object");
            }
        });
        Test("DelayedOldStoreCallbackCannotConfirmReenabledBatch",()=>{
            using(var fixture=new HookFixture())
            {
                Profile(20,fixture.Stats);FakeSteam.Initialized=true;Set(fixture.Stats,"areStatsValid",true);fixture.Update();fixture.Update();
                AchievementFix.DisablePostfix(fixture.Stats);AchievementFix.EnablePostfix(fixture.Stats);fixture.Update();fixture.Update();Equal(FakeSteam.SetIds.Count,2,"new ready session did not reissue pending achievement");
                AchievementFix.StoredPostfix(fixture.Stats,fixture.Callback("UserStatsStored_t",1,1));
                AchievementFix.StoredPostfix(fixture.Stats,fixture.Callback("UserStatsStored_t",1,8));
                AchievementFix.DisablePostfix(fixture.Stats);AchievementFix.EnablePostfix(fixture.Stats);fixture.Update();
                Equal(FakeSteam.SetIds.Count,3,"delayed old success confirmed new failed batch");
            }
        });
        Test("DisableBeforeProfileBindingStillPreventsSubmission",()=>{
            using(var fixture=new HookFixture())
            {
                AchievementFix.DisablePostfix(fixture.Stats);Profile(20,fixture.Stats);FakeSteam.Initialized=true;Set(fixture.Stats,"areStatsValid",true);
                AchievementFix.UpdatePostfix(fixture.Stats);Equal(FakeSteam.SetIds.Count,0,"disabled state lost during profile binding");
                AchievementFix.EnablePostfix(fixture.Stats);AchievementFix.UpdatePostfix(fixture.Stats);Equal(FakeSteam.SetIds.Count,1,"enabled bound profile did not reconcile");
            }
        });
        Test("NativeAllCounterFamiliesReadTheirOwnThresholds",()=>{
            var p=Profile(10,new Listener());var data=(GameData)Get(p,"gameData");
            foreach(string id in new[]{"killImperium10","killImperium50","killImperium100","killImperium250","killImperium500","killImperium1000","playerDeath1","playerVictory1","killTech10","killGangsters10"})data.AchievementData[id]=new Mdi.SRRW.Data.AchievementData{id=id,steamID="fake-"+id};
            Set(p,"imperiumKills",1000);Set(p,"playerDeaths",1);Set(p,"playerVictories",1);Set(p,"techKills",10);Set(p,"gangsterKills",10);
            var earned=AchievementFix.ReadEarned(p);Equal(earned.Count,11,"wrong family eligibility");
            foreach(string id in new[]{"killImperium10","killImperium1000","playerDeath1","playerVictory1","killTech10","killGangsters10","killManufacturers10"})if(!earned.ContainsKey(id))throw new Exception("missing earned "+id);
            if(earned.ContainsKey("playerVictory13")||earned.ContainsKey("playerDeath5"))throw new Exception("unearned counter awarded");
            Equal((int)Get(p,"imperiumKills"),1000,"read changed counter");
        });
        Test("NegativeCountersAndMissingRowsNeverAward",()=>{
            var p=Profile(-1,new Listener());Equal(AchievementFix.ReadEarned(p).Count,0,"negative counter became uint achievement");
            Set(p,"manufacturerKills",20);((GameData)Get(p,"gameData")).AchievementData.Clear();Equal(AchievementFix.ReadEarned(p).Count,0,"missing ID guessed");
        });
        Test("UnexpectedHookFailureDisablesAndReportsOnce",()=>{
            using(var fixture=new HookFixture())
            {
                var report=AchievementFix.Report;try
                {
                    var messages=new List<string>();AchievementFix.Report=messages.Add;Profile(20,fixture.Stats);FakeSteam.ThrowInitialize=true;
                    AchievementFix.UpdatePostfix(fixture.Stats);AchievementFix.UpdatePostfix(fixture.Stats);
                    Equal(AchievementFix.Enabled,false,"unexpected hook error left module active");Equal(messages.Count,1,"hook error not reported exactly once");
                }
                finally{AchievementFix.Report=report;FakeSteam.ThrowInitialize=false;}
            }
        });
        Console.WriteLine("Achievement tests: "+passed+" passed, "+failed+" failed; native managed profile methods and fake adapter only.");return failed==0?0:1;
    }
    static EarnedAchievementQueue Earned(){var q=new EarnedAchievementQueue();q.Reconcile(new Dictionary<string,string>{{"killManufacturers10","srAchvBeatWar10"}});return q;}
    static int Counter(AchievementProfile p){return (int)typeof(AchievementProfile).GetField("manufacturerKills",Flags).GetValue(p);}
    static AchievementProfile Profile(int counter,IAchievementListener listener)
    {
        var p=new AchievementProfile();typeof(AchievementProfile).GetField("manufacturerKills",Flags).SetValue(p,counter);
        var data=(GameData)FormatterServices.GetUninitializedObject(typeof(GameData));var native=new Dictionary<string,Mdi.SRRW.Data.AchievementData>();
        var cached=(Dictionary<string,Mdi.SRRW.Data.Achievements.AchievementData>)typeof(AchievementProfile).GetField("cachedAchievementData",Flags).GetValue(p);
        foreach(int n in new[]{10,25,50,100}){string id="killManufacturers"+n;native[id]=new Mdi.SRRW.Data.AchievementData{id=id,steamID="srAchvBeatWar"+n};cached[id]=new Mdi.SRRW.Data.Achievements.AchievementData{internalId=id,steamId="srAchvBeatWar"+n};}
        typeof(GameData).GetField("<AchievementData>k__BackingField",Flags).SetValue(data,native);
        // Reflection avoids a helper JIT-inlining Initialize before the hook fixture starts.
        typeof(AchievementProfile).GetMethod("Initialize").Invoke(p,new object[]{data,listener});return p;
    }
    sealed class Listener:IAchievementListener{public int TwentyFive;public void UpdateAchievement(Mdi.SRRW.Data.Achievements.AchievementData a){if(a.internalId=="killManufacturers25"&&a.progress==a.goal)TwentyFive++;}}
    sealed class Adapter:IAchievementSubmission
    {
        public bool IsReady=true,QueryResult=true,Owned,SetResult=true;public int StoreRequests;public List<string> SetIds=new List<string>();
        public bool Ready{get{return IsReady;}}public bool TryGet(string id,out bool achieved){achieved=Owned;return QueryResult;}public bool TrySet(string id){SetIds.Add(id);return SetResult;}public void RequestStore(){StoreRequests++;}
    }
    static object Get(object target,string name){return target.GetType().GetField(name,Flags).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Flags).SetValue(target,value);}
    internal static class FakeSteam
    {
        internal static bool Initialized,StoreAccepted=true,ThrowInitialize;internal static int StoreCalls;internal static List<string> SetIds=new List<string>();
        public static bool IsInitialized(){if(ThrowInitialize)throw new InvalidOperationException("synthetic unavailable Steam readiness");return Initialized;}
        public static bool GetAchievement(string id,out bool owned){owned=false;return true;}
        public static bool SetAchievement(string id){SetIds.Add(id);return true;}
        public static bool RequestCurrentStats(){return true;}
        public static bool StoreStats(){StoreCalls++;return StoreAccepted;}
    }
    sealed class HookFixture:IDisposable
    {
        readonly Harmony harmony=new Harmony("test.srbugfix.achievements");readonly Dictionary<string,object> saved=new Dictionary<string,object>();
        internal readonly SteamStats Stats=(SteamStats)FormatterServices.GetUninitializedObject(typeof(SteamStats));
        internal HookFixture()
        {
            var targets=AchievementFix.ResolveTargets();var flags=BindingFlags.Static|BindingFlags.NonPublic;
            foreach(string field in new[]{"initialized","getAchievement","setAchievement"})saved[field]=typeof(AchievementFix).GetField(field,flags).GetValue(null);
            typeof(AchievementFix).GetField("initialized",flags).SetValue(null,typeof(FakeSteam).GetMethod("IsInitialized"));
            typeof(AchievementFix).GetField("getAchievement",flags).SetValue(null,typeof(FakeSteam).GetMethod("GetAchievement"));
            typeof(AchievementFix).GetField("setAchievement",flags).SetValue(null,typeof(FakeSteam).GetMethod("SetAchievement"));
            FakeSteam.Initialized=false;FakeSteam.SetIds.Clear();FakeSteam.StoreAccepted=true;FakeSteam.StoreCalls=0;FakeSteam.ThrowInitialize=false;
            var app=typeof(SteamStats).GetField("appId",Flags);app.SetValue(Stats,Activator.CreateInstance(app.FieldType,new object[]{(ulong)1}));
            harmony.Patch(targets[0],postfix:new HarmonyMethod(typeof(AchievementFix).GetMethod("InitializePostfix",flags)));
            harmony.Patch(targets[1],postfix:new HarmonyMethod(typeof(AchievementFix).GetMethod("RecordPostfix",flags)));
            harmony.Patch(targets[2],prefix:new HarmonyMethod(typeof(AchievementFix).GetMethod("AchievementPrefix",flags)));
            harmony.Patch(targets[3],prefix:new HarmonyMethod(typeof(AchievementFix).GetMethod("UpdatePrefix",flags)),postfix:new HarmonyMethod(typeof(AchievementFix).GetMethod("UpdatePostfix",flags)),transpiler:new HarmonyMethod(typeof(HookFixture).GetMethod("ReplaceExternalCalls",flags)));
            AchievementFix.Enabled=true;
        }
        static IEnumerable<CodeInstruction> ReplaceExternalCalls(IEnumerable<CodeInstruction> instructions)
        {
            int sites=0;
            foreach(var original in instructions)
            {
                var code=new CodeInstruction(original);var method=code.operand as MethodInfo;
                if(method!=null&&method.IsStatic&&(code.opcode==OpCodes.Call||code.opcode==OpCodes.Callvirt))
                {
                    string replacement=null;
                    if(method.DeclaringType.Name=="SteamManager"&&method.Name=="get_Initialized")replacement="IsInitialized";
                    if(method.DeclaringType.FullName=="Steamworks.SteamUserStats"&&(method.Name=="RequestCurrentStats"||method.Name=="StoreStats"))replacement=method.Name;
                    if(replacement!=null){code.opcode=OpCodes.Call;code.operand=typeof(FakeSteam).GetMethod(replacement);sites++;}
                }
                yield return code;
            }
            if(sites!=4)throw new Exception("Native SteamStats.Update external boundary changed");
        }
        internal void Update(){typeof(SteamStats).GetMethod("Update",Flags).Invoke(Stats,null);}
        internal object Callback(string name,ulong app,int result)
        {
            var type=typeof(SteamStats).GetField("appId",Flags).FieldType.Assembly.GetType("Steamworks."+name);var value=Activator.CreateInstance(type);
            type.GetField("m_nGameID").SetValue(value,app);var e=type.GetField("m_eResult");e.SetValue(value,Enum.ToObject(e.FieldType,result));return value;
        }
        public void Dispose(){AchievementFix.Enabled=false;harmony.UnpatchAll(harmony.Id);foreach(var item in saved)typeof(AchievementFix).GetField(item.Key,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,item.Value);}
    }
    static void Equal<T>(T actual,T expected,string reason){if(!EqualityComparer<T>.Default.Equals(actual,expected))throw new Exception(reason+": expected "+expected+", got "+actual);}
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
}
