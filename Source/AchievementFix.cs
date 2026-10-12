using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mdi.SRRW;
using Mdi.SRRW.Data;
using Mdi.SRRW.Data.Achievements;
namespace SRBugfix
{
    internal interface IAchievementSubmission
    {
        bool Ready { get; }
        bool TryGet(string id, out bool achieved);
        bool TrySet(string id);
        void RequestStore();
    }
    internal sealed class EarnedAchievementQueue
    {
        sealed class Entry
        {
            internal string SteamId;
            internal int Attempts;
            internal int Serial;
            internal double NextAttempt;
            internal bool WaitingForStore, Confirmed, Reissue;
        }
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        internal void Reconcile(IDictionary<string,string> earned)
        {
            foreach(string id in entries.Keys.Where(id=>!earned.ContainsKey(id)||earned[id]!=entries[id].SteamId).ToArray())entries.Remove(id);
            foreach(var item in earned)
                if(!string.IsNullOrEmpty(item.Key)&&!string.IsNullOrEmpty(item.Value)&&!entries.ContainsKey(item.Key))
                    entries.Add(item.Key,new Entry{SteamId=item.Value});
        }
        internal void Tick(IAchievementSubmission adapter, double now)
        {
            if(!adapter.Ready||double.IsNaN(now)||double.IsInfinity(now))return;
            foreach(var entry in entries.Values)
            {
                if(entry.Confirmed||entry.WaitingForStore||entry.Attempts>=3||now<entry.NextAttempt)continue;
                entry.Attempts++;entry.Serial++;entry.NextAttempt=now+10;
                try
                {
                    // After a rejected store, Steam's cached ownership can still be true.
                    // Reissue the earned set instead of treating that cache as confirmation.
                    if(!entry.Reissue)
                    {
                        bool owned;
                        if(!adapter.TryGet(entry.SteamId,out owned))continue;
                        if(owned){entry.Confirmed=true;continue;}
                    }
                    if(!adapter.TrySet(entry.SteamId))continue;
                    entry.WaitingForStore=true;adapter.RequestStore();
                }
                catch { /* Treat an unavailable external API like a rejected attempt. */ }
            }
        }
        internal void Stored(bool success, double now)
        {
            CaptureStore().Complete(success,now);
        }
        internal sealed class StoreBatch
        {
            readonly Action<bool,double>[] complete;
            internal StoreBatch(Action<bool,double>[] complete){this.complete=complete;}
            internal void Complete(bool success,double now){foreach(var action in complete)action(success,now);}
        }
        internal StoreBatch CaptureStore()
        {
            var actions=new List<Action<bool,double>>();
            foreach(var entry in entries.Values.Where(e=>e.WaitingForStore))
            {
                var captured=entry;int serial=entry.Serial;
                actions.Add((success,now)=>{
                    if(!captured.WaitingForStore||captured.Serial!=serial)return;
                    captured.WaitingForStore=false;
                    if(success)captured.Confirmed=true;
                    else {captured.Reissue=true;captured.NextAttempt=Math.Max(captured.NextAttempt,now+10);}
                });
            }
            return new StoreBatch(actions.ToArray());
        }
        internal void NewReadySession(double now)
        {
            foreach(var entry in entries.Values.Where(e=>!e.Confirmed))
            {
                if(entry.WaitingForStore){entry.Reissue=true;entry.WaitingForStore=false;}
                entry.Attempts=0;entry.NextAttempt=now;
            }
        }
    }
    public static class AchievementFix
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        sealed class Family
        {
            internal readonly string Counter,Thresholds,Prefix;
            internal readonly int[] Goals;
            internal Family(string counter,string thresholds,string prefix,params int[] goals){Counter=counter;Thresholds=thresholds;Prefix=prefix;Goals=goals;}
        }
        static readonly Family[] families={
            new Family("imperiumKills","imperiumKillThresholds","killImperium",10,50,100,250,500,1000),
            new Family("playerDeaths","playerDeathThresholds","playerDeath",1,5,10,20),
            new Family("playerVictories","playerVictoryThresholds","playerVictory",1,13,25,50,100),
            new Family("techKills","techKillThresholds","killTech",10,25,50,100),
            new Family("gangsterKills","gangsterKillThresholds","killGangsters",10,25,50,100),
            new Family("manufacturerKills","manufacturerKillThresholds","killManufacturers",10,25,50,100)
        };
        internal sealed class Context
        {
            internal WeakReference Profile;
            internal bool Active=true;
            internal bool LastReady;
            internal bool StoreTrackingAmbiguous;
            internal EarnedAchievementQueue Queue=new EarnedAchievementQueue();
            internal readonly Queue<EarnedAchievementQueue.StoreBatch> Stores=new Queue<EarnedAchievementQueue.StoreBatch>();
        }
        internal sealed class UpdateState {internal Context Context;internal EarnedAchievementQueue.StoreBatch Batch;}
        static ConditionalWeakTable<SteamStats,Context> contexts=new ConditionalWeakTable<SteamStats,Context>();
        static readonly Stopwatch clock=Stopwatch.StartNew();
        static FieldInfo valid,store,appId,listener;
        static MethodInfo initialized,getAchievement,setAchievement,gameIdValue;
        internal static bool Enabled;
        internal static Action<string> Report=message=>{};
        static void DisableAfterError(Exception error)
        {
            if(!Enabled)return;Enabled=false;
            var cause=error is TargetInvocationException&&error.InnerException!=null?error.InnerException:error;
            try{Report("Achievement reconciliation disabled after unexpected error: "+cause.GetType().Name+": "+cause.Message);}catch{}
        }

        // Call during the existing guarded startup dry run, before installing any hooks.
        // No Steam API call or profile access is performed by resolution.
        public static MethodInfo[] ResolveTargets()
        {
            Type profile=typeof(AchievementProfile),stats=typeof(SteamStats);
            listener=RequireField(profile,"listener",typeof(IAchievementListener));RequireField(profile,"gameData",typeof(GameData));
            RequireField(profile,"initialized",typeof(bool));
            foreach(var family in families){RequireField(profile,family.Counter,typeof(int));RequireField(profile,family.Thresholds,typeof(Dictionary<string,int>));}
            valid=RequireField(stats,"areStatsValid",typeof(bool));store=RequireField(stats,"shouldStoreStats",typeof(bool));
            appId=stats.GetField("appId",Instance);
            if(appId==null||appId.FieldType.FullName!="Steamworks.CGameID")throw new InvalidDataException("Unsupported achievement app ID.");
            gameIdValue=appId.FieldType.GetMethods(BindingFlags.Public|BindingFlags.Static).SingleOrDefault(m=>m.Name=="op_Explicit"&&m.ReturnType==typeof(ulong)&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==appId.FieldType);
            if(gameIdValue==null)throw new InvalidDataException("Unsupported achievement app ID conversion.");
            Type steamUserStats=appId.FieldType.Assembly.GetType("Steamworks.SteamUserStats");
            Type steamManager=stats.Assembly.GetType("SteamManager")??appId.FieldType.Assembly.GetType("SteamManager");
            var property=steamManager==null?null:steamManager.GetProperty("Initialized",BindingFlags.Public|BindingFlags.Static);
            initialized=property==null?null:property.GetGetMethod();
            getAchievement=steamUserStats==null?null:steamUserStats.GetMethod("GetAchievement",new[]{typeof(string),typeof(bool).MakeByRefType()});
            setAchievement=steamUserStats==null?null:steamUserStats.GetMethod("SetAchievement",new[]{typeof(string)});
            if(initialized==null||initialized.ReturnType!=typeof(bool)||getAchievement==null||getAchievement.ReturnType!=typeof(bool)||!getAchievement.IsStatic||setAchievement==null||setAchievement.ReturnType!=typeof(bool)||!setAchievement.IsStatic)
                throw new InvalidDataException("Unsupported Steam achievement API shape.");
            var received=RequireCallback(stats,"OnUserStatsReceived","Steamworks.UserStatsReceived_t");
            var stored=RequireCallback(stats,"OnUserStatsStored","Steamworks.UserStatsStored_t");
            return new[]{RequireMethod(profile,"Initialize",typeof(GameData),typeof(IAchievementListener)),RequireMethod(profile,"Record",typeof(int).MakeByRefType(),typeof(int),typeof(Dictionary<string,int>)),
                RequireMethod(stats,"UpdateAchievement",typeof(Mdi.SRRW.Data.Achievements.AchievementData)),RequireMethod(stats,"Update"),received,stored,RequireMethod(stats,"OnEnable"),RequireMethod(stats,"OnDisable")};
        }
        static FieldInfo RequireField(Type type,string name,Type expected)
        {
            var field=type.GetField(name,Instance);if(field==null||field.FieldType!=expected)throw new InvalidDataException("Unsupported achievement field: "+name);return field;
        }
        static MethodInfo RequireMethod(Type type,string name,params Type[] parameters)
        {
            var method=type.GetMethod(name,Instance,null,parameters,null);
            if(method==null||method.ReturnType!=typeof(void)||method.IsStatic||method.GetMethodBody()==null)throw new InvalidDataException("Unsupported achievement method: "+name);return method;
        }
        static MethodInfo RequireCallback(Type type,string name,string argument)
        {
            var method=type.GetMethod(name,Instance);var p=method==null?null:method.GetParameters();
            if(p==null||p.Length!=1||p[0].ParameterType.FullName!=argument||method.ReturnType!=typeof(void)||method.IsStatic||method.GetMethodBody()==null)throw new InvalidDataException("Unsupported achievement callback: "+name);
            Type callback=p[0].ParameterType;var id=callback.GetField("m_nGameID");var result=callback.GetField("m_eResult");
            if(id==null||id.FieldType!=typeof(ulong)||result==null||result.FieldType.FullName!="Steamworks.EResult"||!result.FieldType.IsEnum||Convert.ToInt32(Enum.Parse(result.FieldType,"k_EResultOK"))!=1)
                throw new InvalidDataException("Unsupported achievement callback fields: "+name);return method;
        }
        internal static Dictionary<string,string> ReadEarned(AchievementProfile profile)
        {
            var result=new Dictionary<string,string>();
            if(profile==null||!profile.IsInitialized())return result;
            var data=(GameData)RequireField(typeof(AchievementProfile),"gameData",typeof(GameData)).GetValue(profile);
            if(data==null||data.AchievementData==null)return result;
            foreach(var family in families)
            {
                int current=(int)RequireField(typeof(AchievementProfile),family.Counter,typeof(int)).GetValue(profile);
                var thresholds=(Dictionary<string,int>)RequireField(typeof(AchievementProfile),family.Thresholds,typeof(Dictionary<string,int>)).GetValue(profile);
                if(thresholds==null||thresholds.Count!=family.Goals.Length)continue;
                bool shape=family.Goals.All(goal=>thresholds.ContainsKey(family.Prefix+goal)&&thresholds[family.Prefix+goal]==goal);
                if(!shape)continue;
                foreach(int goal in family.Goals)
                {
                    if(current<goal)continue;
                    string id=family.Prefix+goal;Mdi.SRRW.Data.AchievementData row;
                    if(data.AchievementData.TryGetValue(id,out row)&&row!=null&&row.id==id&&!string.IsNullOrEmpty(row.steamID))result[id]=row.steamID;
                }
            }
            return result;
        }
        static bool IsCounter(string id){return families.Any(family=>family.Goals.Any(goal=>family.Prefix+goal==id));}
        static void Bind(AchievementProfile profile,bool initialize)
        {
            if(!Enabled||!profile.IsInitialized())return;
            var stats=listener.GetValue(profile) as SteamStats;if(ReferenceEquals(stats,null))return;
            Context context;
            if(initialize)context=contexts.GetValue(stats,key=>new Context());
            else if(!contexts.TryGetValue(stats,out context)||context.Profile==null||!ReferenceEquals(context.Profile.Target,profile))return;
            if(context.Profile==null||!ReferenceEquals(context.Profile.Target,profile)){context.Profile=new WeakReference(profile);context.Queue=new EarnedAchievementQueue();}
            context.Queue.Reconcile(ReadEarned(profile));
        }
        static void Pump(SteamStats stats)
        {
            if(!Enabled)return;Context context;if(!contexts.TryGetValue(stats,out context)||!context.Active)return;
            var profile=context.Profile==null?null:context.Profile.Target as AchievementProfile;
            context.Queue.Reconcile(ReadEarned(profile));var adapter=new SteamSubmission(stats,context);bool ready=adapter.Ready;
            if(ready&&!context.LastReady)context.Queue.NewReadySession(clock.Elapsed.TotalSeconds);
            context.LastReady=ready;context.Queue.Tick(adapter,clock.Elapsed.TotalSeconds);
        }
        internal static void InitializePostfix(AchievementProfile __instance){try{Bind(__instance,true);}catch(Exception error){DisableAfterError(error);}}
        internal static void RecordPostfix(AchievementProfile __instance){try{Bind(__instance,false);}catch(Exception error){DisableAfterError(error);}}
        internal static bool AchievementPrefix(SteamStats __instance,Mdi.SRRW.Data.Achievements.AchievementData __0)
        {
            if(!Enabled||__0==null||!IsCounter(__0.internalId))return true;
            // Counter submissions use independently read counters rather than trusting a packet.
            try{Context context;if(!contexts.TryGetValue(__instance,out context))return true;context.Queue.Reconcile(ReadEarned(context.Profile==null?null:context.Profile.Target as AchievementProfile));}
            catch(Exception error){DisableAfterError(error);return true;}
            return false;
        }
        internal static void UpdatePrefix(SteamStats __instance,out UpdateState __state)
        {
            __state=null;
            try{Context context;if(Enabled&&contexts.TryGetValue(__instance,out context)&&new SteamSubmission(__instance,context).Ready&&(bool)store.GetValue(__instance))
                __state=new UpdateState{Context=context,Batch=context.Queue.CaptureStore()};}catch(Exception error){DisableAfterError(error);}
        }
        internal static void UpdatePostfix(SteamStats __instance,UpdateState __state=null)
        {
            try
            {
                if(Enabled&&__state!=null&&!(bool)store.GetValue(__instance))
                {
                    // Store callbacks carry no request ID. Preserve ordered batch ownership,
                    // including vanilla-only stores, and suspend extension work if callbacks
                    // disappear rather than growing an unbounded history.
                    if(__state.Context.Stores.Count>=64){__state.Context.Stores.Clear();__state.Context.StoreTrackingAmbiguous=true;}
                    else __state.Context.Stores.Enqueue(__state.Batch);
                }
                Pump(__instance);
            }
            catch(Exception error){DisableAfterError(error);}
        }
        internal static void ReceivedPostfix(SteamStats __instance,object __0)
        {
            try{if(Enabled&&CallbackMatches(__instance,__0)&&CallbackSuccess(__0))Pump(__instance);}catch(Exception error){DisableAfterError(error);}
        }
        internal static void StoredPostfix(SteamStats __instance,object __0)
        {
            try{Context context;if(Enabled&&CallbackMatches(__instance,__0)&&contexts.TryGetValue(__instance,out context)&&context.Active&&context.Stores.Count>0)
                context.Stores.Dequeue().Complete(CallbackSuccess(__0),clock.Elapsed.TotalSeconds);}catch(Exception error){DisableAfterError(error);}
        }
        internal static void EnablePostfix(SteamStats __instance)
        {
            if(!Enabled)return;var context=contexts.GetValue(__instance,key=>new Context());if(!context.Active)
            {
                // Callback disposal does not identify outstanding Steam store requests.
                // Retain their owners so a delayed old success cannot confirm a new batch.
                // If the ownership bound was exceeded, this stats object stays suspended.
                context.Active=true;
            }
        }
        internal static void DisablePostfix(SteamStats __instance)
        {
            if(!Enabled)return;var context=contexts.GetValue(__instance,key=>new Context());context.Active=false;context.LastReady=false;
        }
        internal static bool CallbackMatches(SteamStats stats,object callback)
        {
            return callback!=null&&(ulong)callback.GetType().GetField("m_nGameID").GetValue(callback)==(ulong)gameIdValue.Invoke(null,new[]{appId.GetValue(stats)});
        }
        internal static bool CallbackSuccess(object callback){return Convert.ToInt32(callback.GetType().GetField("m_eResult").GetValue(callback))==1;}
        internal sealed class SteamSubmission:IAchievementSubmission
        {
            readonly SteamStats stats;readonly Context context;
            internal SteamSubmission(SteamStats stats,Context context){this.stats=stats;this.context=context;}
            public bool Ready{get{return context.Active&&!context.StoreTrackingAmbiguous&&(bool)initialized.Invoke(null,null)&&(bool)valid.GetValue(stats);}}
            public bool TryGet(string id,out bool achieved){object[] args={id,false};bool ok=(bool)getAchievement.Invoke(null,args);achieved=(bool)args[1];return ok;}
            public bool TrySet(string id){return (bool)setAchievement.Invoke(null,new object[]{id});}
            public void RequestStore(){store.SetValue(stats,true);}
        }
    }
}
