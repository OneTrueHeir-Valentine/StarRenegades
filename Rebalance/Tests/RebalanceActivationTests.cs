using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
using SRBugfix;

internal static class RebalanceActivationTests
{
    const string Owner="local.star-renegades.rebalance";
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    static string gameRoot,sourceRoot,coreSourceRoot,modRoot;
    static int passed,failed;
    static readonly string[] Tables={"EquipmentData","PowerModifierData","CamaraderieStringData","TraitData","TagGroupData","PowerData","UnitClassData","UnitModifierData","TraitStringData","PowerStringData","CamaraderieCardData","UnitCombatActionData","EffectData"};
    static int Main(string[] args)
    {
        gameRoot=args[0];sourceRoot=args[1];coreSourceRoot=args[2];
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);
            if(!File.Exists(path))path=Path.Combine(gameRoot,"Star Renegades_Data/Managed/"+name);
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        modRoot=Temporary("activation-manifests");
        File.Copy(Path.Combine(coreSourceRoot,"fixes.json"),Path.Combine(modRoot,"fixes.json"),true);
        File.Copy(Path.Combine(sourceRoot,"rebalance.json"),Path.Combine(modRoot,"rebalance.json"),true);
        Test("CombinedManifestApplies483FieldsAnd61Additions",()=>{
            JObject core,balance;var d=Fixture(out core,out balance);
            var equipment=d.EquipmentData;var sword=d.TagGroupData["tagSwordElementalA3"];
            var result=Activate(d,core,balance,new Action[0],()=>{});
            Check(result.ChangedFields==483&&result.AddedRecords==61,"expected 483 fields and 61 additions, got "+result.ChangedFields+" / "+result.AddedRecords);
            Check(d.UnitClassData["hMar"].shield==40&&d.UnitClassData["hMar"].shieldCap==40,"Marksman shields missing");
            Check(!d.TraitData["hValGueTraits"].hidden&&d.PowerModifierData["hValGue"].modExecution==-5,"Dayana balance missing");
            Check(d.PowerData["hComInstantAttack"].range=="Any","Salvo range missing");
            Check(d.PowerData["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanGueCombo","shared Illuminate correction missing");
            Check(d.PowerStringData["hGrdSpeComboDesc"].EN=="Wipe all enemy buffs and all hero debuffs.","shared Hard Reset English correction missing");
            Check(d.EffectData["hDarSpeCombo"].tagsHash.Contains("wipedByAttacking"),"shared Shadow Puppets cancellation missing");
            Check(d.UnitCombatActionData["oVanSummonA"].nextAction[0]=="oVanSummonB","enemy continuation missing");
            Check(d.PowerModifierData["camParTrait1"].setCriticalTransferShieldAbsolute==0,"core correction missing");
            Check(d.TagGroupData["tagKineticElementalA3"].tagsHash.Contains("kinetic"),"shared affix fix missing");
            Check(ReferenceEquals(sword,d.TagGroupData[sword.id]),"core pool template replaced");
            Check(!ReferenceEquals(equipment,d.EquipmentData),"equipment correction not committed");
            foreach(var e in ((JArray)balance["changes"]).Concat((JArray)core["changes"])){
                var row=Table(d,(string)e["table"])[(string)e["id"]];var f=row.GetType().GetField((string)e["field"]);
                object expected=e["after"].ToObject(f.FieldType),actual=f.GetValue(row);
                Check(actual is string[]?((string[])actual).SequenceEqual((string[])expected):Equals(actual,expected),"wrong field "+e["table"]+"/"+e["id"]+"/"+e["field"]);
            }
        });
        Test("BadBalanceGuardRestoresCoreAndEnemiesBeforeHooks",()=>{
            JObject core,balance;var d=Fixture(out core,out balance);var originals=Snapshot(d);
            ((JObject)((JArray)balance["changes"]).Last)["before"]=9999;
            bool runtimeInstalled=false;
            Reject(()=>Activate(d,core,balance,new Action[]{()=>runtimeInstalled=true},()=>{}),"before");
            Check(!runtimeInstalled,"runtime hooks attempted after failed balance guard");Restored(d,originals);
        });
        Test("LateHookFailureRestoresAllTablesAndRuntimeFlags",()=>{
            JObject core,balance;var d=Fixture(out core,out balance);var originals=Snapshot(d);var h=new Harmony(Owner);
            var actions=RuntimeFixes.Prepare(h,s=>{}).Concat(new Action[]{()=>{
                Check(d.UnitClassData["hMar"].shield==40,"balance not committed before runtime hooks");
                RuntimeFixes.SetEnabled(true);ComboEffectFix.Enabled=true;throw new InvalidOperationException("late hook failure");
            }}).ToArray();
            try{Activate(d,core,balance,actions,()=>RuntimeFixes.Remove(h));throw new Exception("failure not propagated");}
            catch(InvalidOperationException e){Check(e.Message=="late hook failure","wrong hook failure");}
            Restored(d,originals);
            Check(!PreviewFix.Enabled&&!ReflectionPreviewFix.Enabled&&!DamageInspectionFix.Enabled&&!ProjectedEffectFix.Enabled&&!AchievementFix.Enabled&&!EnemyAiFix.Enabled&&!ComboEffectFix.Enabled,"helper flags remain enabled");
            Check(!ResourceForecastFix.Enabled&&!EffectLifecycleFix.Enabled,"priority 2 helper flags remain enabled");
            Check(!Harmony.GetAllPatchedMethods().Any(Patched),"runtime hooks remain installed");
        });
        Test("BothPinnedManifestsAcceptedAndTamperingRejected",()=>{
            Check((string)SRBugfix.Bootstrap.Read(gameRoot,modRoot)["version"]=="0.1.4","wrong core manifest version");
            Check((string)ReadBalance(modRoot)["version"]=="0.3.3","wrong balance manifest version");
            string bad=Temporary("tampered");
            File.WriteAllText(Path.Combine(bad,"fixes.json"),File.ReadAllText(Path.Combine(modRoot,"fixes.json"))+" ");
            Reject(()=>SRBugfix.Bootstrap.Read(gameRoot,bad),"checksum");
            File.WriteAllText(Path.Combine(bad,"rebalance.json"),File.ReadAllText(Path.Combine(modRoot,"rebalance.json"))+" ");
            Reject(()=>ReadBalance(bad),"checksum");
        });
        Test("EntrypointInstallsLoaderOnlyBeforeDataActivation",()=>{
            PrepareEntrypoint();Install();
            Check(Patched(Loader()),"loader hook missing");
            Check(!Patched(ReflectionPreviewFix.ResolveTarget())&&!Patched(AchievementFix.ResolveTargets()[0]),"runtime hooks installed before activation");
            Check(!PreviewFix.Enabled&&!ProjectedEffectFix.Enabled&&!AchievementFix.Enabled&&!EnemyAiFix.Enabled&&!ComboEffectFix.Enabled,"helper enabled before activation");
            Check(!ResourceForecastFix.Enabled&&!EffectLifecycleFix.Enabled,"priority 2 helpers enabled before activation");
        });
        Test("EntrypointEnablesAllSharedRuntimeFixesAfterBalance",()=>{
            PrepareEntrypoint();Install();JObject core,balance;var d=Fixture(out core,out balance);
            Set("coreManifest",core);Set("balanceManifest",balance);InvokeEntrypoint("AfterDataLoad",d);
            Check(PreviewFix.Enabled&&ReflectionPreviewFix.Enabled&&DamageInspectionFix.Enabled&&ProjectedEffectFix.Enabled&&AchievementFix.Enabled&&EnemyAiFix.Enabled&&ComboEffectFix.Enabled,"shared helpers not all enabled");
            Check(ResourceForecastFix.Enabled&&EffectLifecycleFix.Enabled,"priority 2 helpers not enabled");
            foreach(var m in AchievementFix.ResolveTargets())Check(Patched(m),"achievement hook missing "+m.Name);
            Check(Patched(typeof(Mdi.SRRW.Engine.Combat.CombatAI).GetMethod("SelectAction",BindingFlags.Instance|BindingFlags.NonPublic)),"weighted enemy hook missing");
            Check(Patched(ComboEffectFix.ResolveTarget())&&Patched(ComboEffectFix.ResolveForecastTarget()),"shared combo hooks missing");
            Check(d.UnitClassData["hMar"].shield==40,"balance missing after loader activation");
        });
        Test("EntrypointLateHookFailureRestoresOriginalState",()=>{
            PrepareEntrypoint();Install();JObject core,balance;var d=Fixture(out core,out balance);var originals=Snapshot(d);
            Set("coreManifest",core);Set("balanceManifest",balance);
            var actions=(Action[])Entrypoint().GetField("installHooks",Static).GetValue(null);
            Set("installHooks",actions.Concat(new Action[]{()=>{throw new InvalidOperationException("entrypoint late failure");}}).ToArray());
            InvokeEntrypoint("AfterDataLoad",d);Restored(d,originals);
            Check(!PreviewFix.Enabled&&!ProjectedEffectFix.Enabled&&!AchievementFix.Enabled&&!EnemyAiFix.Enabled&&!ComboEffectFix.Enabled,"entrypoint enabled helpers after failure");
            Check(!ResourceForecastFix.Enabled&&!EffectLifecycleFix.Enabled,"priority 2 helpers enabled after failure");
            Check(!Harmony.GetAllPatchedMethods().Any(Patched),"entrypoint left hooks installed");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static Overlay.StageResult Activate(GameData d,JObject core,JObject balance,Action[] hooks,Action remove)
    {
        var type=Assembly.GetExecutingAssembly().GetType("SRRebalance.RebalanceActivation");
        if(type==null)return SRBugfix.Activation.Apply(d,core,hooks,remove);
        try{return (Overlay.StageResult)type.GetMethod("Apply").Invoke(null,new object[]{d,core,balance,hooks,remove});}
        catch(TargetInvocationException e){throw e.InnerException;}
    }
    static JObject ReadBalance(string dir)
    {
        var type=Assembly.GetExecutingAssembly().GetType("SRRebalance.Bootstrap");Check(type!=null,"balance bootstrap missing");
        try{return (JObject)type.GetMethod("Read").Invoke(null,new object[]{gameRoot,dir});}catch(TargetInvocationException e){throw e.InnerException;}
    }
    static GameData Fixture(out JObject core,out JObject balance)
    {
        core=JObject.Parse(File.ReadAllText(Path.Combine(coreSourceRoot,"fixes.json")));
        balance=JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot,"rebalance.json")));
        var d=CoreTests.Fixture(core);
        foreach(string t in Tables){var p=typeof(GameData).GetProperty(t);if(p.GetValue(d,null)==null)p.GetSetMethod(true).Invoke(d,new[]{Activator.CreateInstance(p.PropertyType)});}
        foreach(var e in (JArray)balance["changes"]){var table=Table(d,(string)e["table"]);string id=(string)e["id"];if(!table.Contains(id))table.Add(id,NewRow(table.GetType().GetGenericArguments()[1],id));var row=table[id];var f=row.GetType().GetField((string)e["field"]);f.SetValue(row,e["before"].ToObject(f.FieldType));}
        d.PowerModifierData["hMarVanTraits"]=(PowerModifierData)NewRow(typeof(PowerModifierData),"hMarVanTraits");
        var actions=d.UnitCombatActionData;
        actions["oVanSummonA"]=new UnitCombatActionData{id="oVanSummonA",nextAction=new[]{"oVanBSummonB"}};
        actions["ePanActRepair"]=new UnitCombatActionData{id="ePanActRepair",nextAction=new[]{"ePanActScattershot","ePanActFlurry","ePanActAoeFlurry"}};
        foreach(string id in new[]{"oVanSummonB","ePanActScattershot","ePanActFlurry","ePanActAoeAttack"})actions[id]=new UnitCombatActionData{id=id};
        foreach(string t in Tables){int count=Table(d,t).Count;if(core["dictionary_counts"][t]!=null)core["dictionary_counts"][t]=count;if(balance["dictionary_counts"][t]!=null)balance["dictionary_counts"][t]=count;}
        return d;
    }
    static string Temporary(string name){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    static object NewRow(Type type,string id){var row=Activator.CreateInstance(type);foreach(var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance))if(f.FieldType==typeof(string))f.SetValue(row,"");else if(f.FieldType==typeof(string[]))f.SetValue(row,new string[0]);type.GetField("id").SetValue(row,id);return row;}
    static Type Entrypoint(){var t=Assembly.GetExecutingAssembly().GetType("Doorstop.Entrypoint");Check(t!=null,"Rebalance entrypoint missing");return t;}
    static void PrepareEntrypoint(){RuntimeFixes.Remove(new Harmony(Owner));Set("root",modRoot);Set("gameRoot",gameRoot);Set("applications",0);}
    static void Set(string n,object v){Entrypoint().GetField(n,Static).SetValue(null,v);}
    static void Install(){InvokeEntrypoint("Install");}
    static void InvokeEntrypoint(string n,params object[] a){try{Entrypoint().GetMethod(n,Static).Invoke(null,a);}catch(TargetInvocationException e){throw e.InnerException;}}
    static MethodBase Loader(){return typeof(GameData).GetMethod("LoadData",BindingFlags.Instance|BindingFlags.NonPublic);}
    static bool Patched(MethodBase m){var i=Harmony.GetPatchInfo(m);return i!=null&&i.Owners.Contains(Owner);}
    static IDictionary Table(GameData d,string t){return (IDictionary)typeof(GameData).GetProperty(t).GetValue(d,null);}
    static Dictionary<string,IDictionary> Snapshot(GameData d){return Tables.ToDictionary(t=>t,t=>Table(d,t));}
    static void Restored(GameData d,Dictionary<string,IDictionary> originals){foreach(string t in Tables)Check(ReferenceEquals(originals[t],Table(d,t)),"original dictionary not restored "+t);Check(d.UnitClassData["hMar"].shield==15,"original balance field changed");}
    static void Check(bool c,string m){if(!c)throw new Exception(m);}
    static void Reject(Action a,string word){try{a();}catch(InvalidDataException e){Check(e.Message.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0,"wrong rejection "+e.Message);return;}throw new Exception("expected rejection "+word);}
    static void Test(string n,Action a){try{a();passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.Message);}finally{RuntimeFixes.Remove(new Harmony(Owner));}}
}
