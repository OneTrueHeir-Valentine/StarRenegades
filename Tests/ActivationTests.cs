using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Newtonsoft.Json.Linq;
using SRBugfix;

internal static class ActivationTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const string Owner = "local.star-renegades.bugfix";
    static int passed, failed;
    static string gameRoot, sourceRoot;
    static int Main(string[] args)
    {
        gameRoot = args[0]; sourceRoot = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (!File.Exists(path)) path = Path.Combine(gameRoot, "Star Renegades_Data/Managed/" + name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Test("StartupPreflightInstallsOnlyDataLoader", () => {
            Prepare(); Install();
            Check(Patched(Loader()), "data loader hook missing");
            Check(!Patched(ReflectionPreviewFix.ResolveTarget()), "runtime forecast hook installed before data activation");
            Check(!Patched(AchievementFix.ResolveTargets()[0]), "achievement hook installed before data activation");
            Check(!ProjectedEffectFix.Enabled && !AchievementFix.Enabled, "new helpers enabled before activation");
            Check(!ComboEffectFix.Enabled&&!Patched(ComboEffectFix.ResolveTarget()),"combo routing installed before activation");
        });
        Test("ActivationIncludesProjectedAndAchievementFixes", () => {
            Prepare(); Install(); var data = Fixture(); Apply(data);
            Check(ProjectedEffectFix.Enabled && AchievementFix.Enabled, "new fixes not enabled after successful activation");
            Check(PreviewFix.Enabled && ReflectionPreviewFix.Enabled && DamageInspectionFix.Enabled, "existing fixes disabled");
            Check(ComboEffectFix.Enabled&&Patched(ComboEffectFix.ResolveTarget())&&Patched(ComboEffectFix.ResolveForecastTarget()),"combo routing not enabled after activation");
            Check(Patched(typeof(Combat.CombatUnit.EstimatedState).GetConstructor(new[] { typeof(Combat.CombatUnit) })), "projected state hook missing");
            Check(Patched(typeof(Combat).GetMethod("GetPowerReactions")), "reaction amount hook missing");
            foreach (var method in AchievementFix.ResolveTargets()) Check(Patched(method), "achievement hook missing: " + method.Name);
            Check(data.PowerModifierData["camParTrait1"].setCriticalTransferShieldAbsolute == 0, "core fixes missing");
            Check(data.TagGroupData.Count == 48, "affix additions missing");
        });
        Test("LastHookFailureRestoresAllDataAndHooks", () => {
            Prepare(); Install(); var data = Fixture();
            var equipment = data.EquipmentData; var traits = data.TraitData; var groups = data.TagGroupData; var enemyActions = data.UnitCombatActionData; var powers=data.PowerData;var descriptions=data.PowerStringData;var effects=data.EffectData;
            var field = typeof(Doorstop.Entrypoint).GetField("installHooks", Static);
            var actions = (Action[])field.GetValue(null);
            field.SetValue(null, actions.Concat(new Action[] { () => { throw new InvalidOperationException("last-hook fixture failure"); } }).ToArray());
            Apply(data);
            Check(ReferenceEquals(equipment, data.EquipmentData) && ReferenceEquals(traits, data.TraitData) && ReferenceEquals(groups, data.TagGroupData), "failed activation left changed data");
            Check(ReferenceEquals(enemyActions,data.UnitCombatActionData),"failed activation left changed enemy actions");
            Check(ReferenceEquals(powers,data.PowerData)&&ReferenceEquals(descriptions,data.PowerStringData)&&ReferenceEquals(effects,data.EffectData),"failed activation left changed combo tables");
            Check(!PreviewFix.Enabled && !ReflectionPreviewFix.Enabled && !DamageInspectionFix.Enabled && !ProjectedEffectFix.Enabled && !AchievementFix.Enabled, "failed activation left enabled helpers");
            Check(!ComboEffectFix.Enabled,"failed activation left combo helper enabled");
            Check(!Harmony.GetAllPatchedMethods().Any(Patched), "failed activation left hooks installed");
        });
        Test("ActivationIncludesEnemySequencesAndWeightedSelection", () => {
            Prepare();Install();var data=Fixture();Apply(data);
            Check(data.UnitCombatActionData["oVanSummonA"].nextAction[0]=="oVanSummonB","Dragoon continuation missing at activation");
            Check(data.UnitCombatActionData["ePanActRepair"].nextAction[2]=="ePanActAoeAttack","Panzer continuation missing at activation");
            Check(Patched(typeof(Combat.CombatAI).GetMethod("SelectAction",BindingFlags.Instance|BindingFlags.NonPublic)),"weighted enemy selector not installed");
            var type=typeof(Doorstop.Entrypoint).Assembly.GetType("SRBugfix.EnemyAiFix");
            Check(type!=null&&(bool)type.GetField("Enabled").GetValue(null),"enemy helper not enabled");
        });
        Test("ActivationRepairsIlluminateAndHardResetEnglish",()=>{
            Prepare();Install();var data=Fixture();Apply(data);
            Check(data.PowerData["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanGueCombo","Illuminate still deactivates the other combo");
            Check(data.PowerStringData["hGrdSpeComboDesc"].EN=="Wipe all enemy buffs and all hero debuffs.","Hard Reset text still promises missing recharge");
            Check(data.EffectData["hDarSpeCombo"].tagsHash.Contains("wipedByAttacking"),"Shadow Puppets cancellation missing");
        });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
    static void Prepare()
    {
        new Harmony(Owner).UnpatchAll(Owner);
        RuntimeFixes.SetEnabled(false);ComboEffectFix.Enabled=false;
        var enemyType=typeof(Doorstop.Entrypoint).Assembly.GetType("SRBugfix.EnemyAiFix");
        if(enemyType!=null)enemyType.GetField("Enabled").SetValue(null,false);
        Set("root", sourceRoot); Set("gameRoot", gameRoot); Set("applications", 0);
    }
    static void Set(string name, object value) { typeof(Doorstop.Entrypoint).GetField(name, Static).SetValue(null, value); }
    static void Install() { Invoke("Install"); }
    static void Apply(GameData data) { Invoke("AfterDataLoad", data); }
    static void Invoke(string name, params object[] args)
    {
        try { typeof(Doorstop.Entrypoint).GetMethod(name, Static).Invoke(null, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static GameData Fixture()
    {
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot, "fixes.json")));
        // The small fixture keeps the production correction list but uses its own table counts.
        var data = CoreTests.Fixture(manifest);
        var enemyActions=new System.Collections.Generic.Dictionary<string,UnitCombatActionData>{
            {"oVanSummonA",new UnitCombatActionData{id="oVanSummonA",nextAction=new[]{"oVanBSummonB"}}},
            {"oVanSummonB",new UnitCombatActionData{id="oVanSummonB"}},
            {"ePanActRepair",new UnitCombatActionData{id="ePanActRepair",nextAction=new[]{"ePanActScattershot","ePanActFlurry","ePanActAoeFlurry"}}},
            {"ePanActScattershot",new UnitCombatActionData{id="ePanActScattershot"}},
            {"ePanActFlurry",new UnitCombatActionData{id="ePanActFlurry"}},
            {"ePanActAoeAttack",new UnitCombatActionData{id="ePanActAoeAttack"}}
        };
        typeof(GameData).GetProperty("UnitCombatActionData").GetSetMethod(true).Invoke(data,new object[]{enemyActions});
        Set("manifest", manifest); return data;
    }
    static MethodBase Loader() { return typeof(GameData).GetMethod("LoadData", BindingFlags.Instance | BindingFlags.NonPublic); }
    static bool Patched(MethodBase method) { var info = Harmony.GetPatchInfo(method); return info != null && info.Owners.Contains(Owner); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
        finally { new Harmony(Owner).UnpatchAll(Owner); }
    }
}
