using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
#if !NATIVE_BASELINE
using SRBugfix;
#endif

// Actual native energy predicate and ScheduleUnits. The oVan action rows below
// are hand-checked current data. Only RNG, availability and live-combat sinks
// are intercepted; energy getters, comparison and action selection remain native.
internal static class EnemyConditionTests
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static int passed,failed,index,rngCalls,loops,noActions;
 static string scheduled;
 static SRRWGame game;
 static GameData data;
 static Dictionary<string,UnitCombatActionData> actions;
 static Dictionary<string,Power> powers;
 static Combat.CombatAI ai;
 static Combat.CombatUnit cu;
 static MethodInfo predicate;
 static int Main(string[] args){string managed=Path.Combine(args[0],"Star Renegades_Data/Managed");AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,new AssemblyName(e.Name).Name+".dll");if(!File.Exists(file))file=Path.Combine(managed,new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};return Run();}
 [MethodImpl(MethodImplOptions.NoInlining)]
 static int Run(){
  var h=new Harmony("test.srbugfix.enemy-condition");
  predicate=typeof(Combat.CombatAI).GetMethod("DoesUnitSatisfyIfEnergyAction",F);
  using(var hash=SHA256.Create())Console.WriteLine("NATIVE_IL_SHA256 "+BitConverter.ToString(hash.ComputeHash(predicate.GetMethodBody().GetILAsByteArray())).Replace("-","").ToLowerInvariant());
  try{
   Hook(h,typeof(Mdi.SRRW.Engine.Random).GetMethod("Range",new[]{typeof(int),typeof(int)}),"Range");
   Hook(h,typeof(Combat.CombatUnitPower).GetMethod("IsUsable",F),"Usable");
   Hook(h,typeof(Combat.CombatAI).GetMethod("GetTargetForAction",F),"Target");
   Hook(h,typeof(Combat.CombatUnitPower).GetMethod("Schedule",F,null,new[]{typeof(Combat.CombatUnitPower.TargetGroup)},null),"Schedule");
   foreach(string name in new[]{"ApplyConditionalEffects","SetEffectForAction"})Hook(h,typeof(Combat.CombatAI).GetMethod(name,F),"Skip");
   Hook(h,typeof(Combat.CombatAI).GetMethod("NotifyInfiniteLoop",F),"Loop");
   Hook(h,typeof(Combat.CombatAI).GetMethod("PerformNoActionForUnit",F),"NoAction");
   Hook(h,typeof(UnityEngine.Debug).GetMethod("Log",new[]{typeof(object)}),"Skip");
   // Capture legacy outputs before installing/enabling the repair. These inputs
   // must still take the original path, including NaN and denominator behavior.
   var invalid=new[]{new[]{0f,0f,60f},new[]{1f,0f,60f},new[]{30f,-50f,60f},new[]{float.NaN,50f,60f},new[]{float.PositiveInfinity,50f,60f},new[]{30f,float.NaN,60f},new[]{30f,float.PositiveInfinity,60f},new[]{30f,50f,float.NaN},new[]{30f,50f,float.PositiveInfinity}};
   string[] operators={"LessThan","LessThanEqualTo","EqualTo","GreaterThanEqualTo","GreaterThan"};
   var nativeInvalid=new bool[invalid.Length,operators.Length];
   for(int i=0;i<invalid.Length;i++)for(int j=0;j<operators.Length;j++)nativeInvalid[i,j]=Evaluate(invalid[i][0],invalid[i][1],invalid[i][2],operators[j]);
#if !NATIVE_BASELINE
   Test("ExactNativeEnergyTargetValidatedBeforeInstallation",()=>{Check(!EnemyConditionFix.Enabled,"repair enabled before activation");Check(EnemyConditionFix.ResolveTarget()==predicate,"wrong native predicate");EnemyConditionFix.ValidateTargets();EnemyConditionFix.Install(h);});
#endif
   Test("DisabledExactThresholdRetainsNativeGreaterThanRounding",()=>Check(Evaluate(30,50,60,"GreaterThan"),"disabled hook changed native boundary"));
#if !NATIVE_BASELINE
   EnemyConditionFix.Enabled=true;
#endif
   // Removing this repair, using >=, or rounding the ratio back to float fails
   // these literal mathematical expectations at the current 30/50 threshold.
   bool[][] want={new[]{true,true,false,false,false},new[]{false,true,true,true,false},new[]{false,false,false,true,true}};
   int[] energies={29,30,31};
   for(int i=0;i<energies.Length;i++)for(int j=0;j<operators.Length;j++){
    int energy=energies[i];string op=operators[j];bool expected=want[i][j];
    Test("Fury"+energy+"Of50"+op+"60",()=>Check(Evaluate(energy,50,60,op)==expected,"expected "+expected+" at "+energy+"/50 "+op+"60"));
   }
   Test("GreaterThanUsesRuntimeCeilingMaximum",()=>Check(!Evaluate(23f,37.25f,61,"GreaterThan"),"ignored native rounded maximum"));
   Test("GreaterThanUsesRuntimeFloorCurrentAndMaximum",()=>Check(!Evaluate(22.75f,37.25f,60,"GreaterThan",false),"ignored native rounded current or maximum"));
   Test("UnknownEnergyOperatorRemainsFalse",()=>{Prepare(30,50,true);var a=Action("probe",new string[0],new string[0],new string[0]);typeof(UnitCombatActionData).GetProperty("IfEnergyCondition",F).GetSetMethod(true).Invoke(a,new object[]{(UnitCombatActionData.EnergyCondition)999});Check(!Eval(a),"unknown enum changed");});
   for(int i=0;i<invalid.Length;i++)for(int j=0;j<operators.Length;j++){
    int sample=i,op=j;Test("InvalidDomain"+sample+operators[op]+"RetainsNative",()=>Check(Evaluate(invalid[sample][0],invalid[sample][1],invalid[sample][2],operators[op])==nativeInvalid[sample,op],"legacy invalid-domain comparison changed"));
   }
   // Literal action selection is the observable change, beyond a numeric helper.
   foreach(int energy in energies){int value=energy;Test("NativeScheduleFury"+value+"Of50",()=>{Prepare(value,50,true);ai.ScheduleUnits();string expected=value==31?"oVanSupercharge":"oVanAttack";Check(scheduled==expected,"expected "+expected+", got "+scheduled);Check(loops==0&&noActions==0,"unexpected fallback failure");});}
#if !NATIVE_BASELINE
   EnemyConditionFix.Enabled=false;
#endif
   Test("DisablingInstalledHookRestoresNativeThreshold",()=>Check(Evaluate(30,50,60,"GreaterThan"),"disabled hook did not restore native"));
  }finally{
#if !NATIVE_BASELINE
   EnemyConditionFix.Enabled=false;
#endif
   h.UnpatchAll(h.Id);
  }
  Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed; native energy predicate and scheduling, artificial availability, isolated process");return failed==0?0:1;
 }
 static UnitCombatActionData Action(string id,string[] power,string[] next,string[] fallback){var a=A(id,"unused",fallback);a.powerIDs=power;a.nextAction=next;a.OnAfterDeserialize();return a;}
 static void Prepare(float energy,float maximum,bool roundUp){
  var check=Action("oVanStartCheck",new string[0],new[]{"oVanBasic"},new[]{"oVanBasic"});check.ifLowHealthAction=new[]{"oVanSummonA"};check.ifLowHealthPercent=75;check.ifLowHealthCancelPower=true;check.ifEnergyAction=new[]{"oVanSupercharge"};check.ifEnergyCondition="GreaterThan";check.ifEnergyPercent=60;check.OnAfterDeserialize();
  var basic=Action("oVanBasic",new[]{"oVanSuperchargeCancel"},new[]{"oVanBasicAttack","oVanBasicWeaken","oVanBasicAOE","oVanBasicFlurry"},new[]{"oVanBasicAttack","oVanBasicWeaken","oVanBasicAOE","oVanBasicFlurry"});basic.ifEnergyAction=new[]{"oVanSupercharge"};basic.ifEnergyCondition="GreaterThan";basic.ifEnergyPercent=60;basic.OnAfterDeserialize();
  var rows=new List<UnitCombatActionData>{check,basic,Action("oVanSupercharge",new[]{"oVanSupercharge"},new[]{"oVanSuperAttack","oVanSuperWeaken","oVanSuperAOE","oVanSuperFlurry"},new[]{"oVanStartCheck"})};
  string[] ids={"oVanBasicAttack","oVanBasicWeaken","oVanBasicAOE","oVanBasicFlurry"};string[] powerIds={"oVanAttack","oVanWeaken","oVanAOE","oVanFlurry"};
  for(int i=0;i<ids.Length;i++){var row=Action(ids[i],new[]{powerIds[i]},new[]{"oVanStartCheck"},new[]{"oVanSupercharge"});row.ifAllyLowHealthAction=new[]{"oVanAuraHealthDrain"};row.ifAllyLowHealthPercent=40;rows.Add(row);}
  Fixture(rows.ToArray(),"oVanStartCheck",new[]{"oVanAttack","oVanSupercharge"});Set(cu.Unit,"currentHealth",100);Set(cu.Unit,"maximumHealth",new ModifiedValue(100));Set(cu.Unit,"energyTypeData",new EnergyTypeData{id="oVan",roundUp=roundUp});Set(cu.Unit,"currentEnergy",energy);Set(cu.Unit,"maximumEnergy",new ModifiedValue(maximum));
 }
 static bool Evaluate(float energy,float maximum,float percent,string op,bool roundUp=true){Prepare(energy,maximum,roundUp);var a=actions["oVanStartCheck"];a.ifEnergyPercent=percent;a.ifEnergyCondition=op;a.OnAfterDeserialize();return Eval(a);}
 static bool Eval(UnitCombatActionData a){return (bool)predicate.Invoke(ai,new object[]{cu,a});}
 static UnitCombatActionData A(string id,string power,string[] fallback){var a=new UnitCombatActionData{id=id,powerIDs=new[]{power},ifUnusableAction=fallback};foreach(var f in typeof(UnitCombatActionData).GetFields(F))if(f.FieldType==typeof(string[])&&f.GetValue(a)==null)f.SetValue(a,new string[0]);a.OnAfterDeserialize();return a;}
 static void Fixture(UnitCombatActionData[] rows,string first,string[] usable){
  index=rngCalls=loops=noActions=0;scheduled=null;actions=rows.ToDictionary(x=>x.id);data=Blank<GameData>();Property(data,"UnitCombatActionData",actions);Property(data,"PowerData",new Dictionary<string,PowerData>());Property(data,"EffectData",new Dictionary<string,EffectData>());Property(data,"VulnerabilityData",new Dictionary<string,VulnerabilityData>());Property(data,"EnergyTypeData",new Dictionary<string,EnergyTypeData>());
  game=Blank<SRRWGame>();Set(game,"gameData",data);powers=new Dictionary<string,Power>();Set(game,"powers",powers);Set(game,"effects",new Dictionary<string,Effect>());Set(game,"vulnerabilities",new Dictionary<string,Vulnerability>());
  var squad=Blank<Squad>();Init(squad,game,"fixture-squad");Set(squad,"units",new Unit[6]);
  var unit=Blank<Unit>();Init(unit,game,"fixture-unit");Set(unit,"unitData",new UnitData{id="fixture-unit"});Set(unit,"defaultCombatActions",new HashSet<string>{first});Set(unit,"squad",squad);
  var team=Blank<Combat.CombatTeam>();Init(team,game,"fixture-team");Set(team,"squad",squad);
  cu=Blank<Combat.CombatUnit>();Init(cu,game,"fixture-unit");Set(cu,"unit",unit);Set(cu,"team",team);Set(cu,"state",Combat.CombatUnit.UnitState.Ready);((GameElementCollection<Combat.CombatUnit>)Field(team,"units").GetValue(team)).Add(cu);
  foreach(string id in usable){var p=Blank<Power>();Init(p,game,id);Set(p,"powerData",new PowerData{id=id});powers[id]=p;var up=Blank<UnitPower>();Init(up,game,id);Set(up,"power",p);Set(up,"unit",unit);Set(up,"tags",new HashSet<string>());var cp=Blank<Combat.CombatUnitPower>();Init(cp,game,id);Set(cp,"unit",cu);Set(cp,"power",up);((GameElementCollection<Combat.CombatUnitPower>)Field(cu,"powers").GetValue(cu)).Add(cp);}
  ai=Blank<Combat.CombatAI>();Init(ai,game,"fixture-ai");Set(ai,"team",team);
  foreach(string n in new[]{"lastUnitActions","overrideUnitActions","interruptedUnits","lowHealthUnits","ifEnergyUnits"})Set(ai,n,new Dictionary<Combat.CombatUnit,UnitCombatActionData>());Set(ai,"overrideUnitActions",new Dictionary<Combat.CombatUnit,UnitCombatActionData>{{cu,actions[first]}});Set(ai,"effectsForUnitActions",new Dictionary<Combat.CombatUnit,Effect>());
  Set(ai,"selectableActions",new List<string>());Set(ai,"candidateActions",new List<string>());Set(ai,"candidateProbabilities",new List<int>());
 }
 static UnitCombatActionData Last(){return ((Dictionary<Combat.CombatUnit,UnitCombatActionData>)Field(ai,"lastUnitActions").GetValue(ai))[cu];}
 static bool Range(int __0,int __1,ref int __result){rngCalls++;__result=Math.Min(index,__1-1);return false;}
 static bool Usable(ref bool __result){__result=true;return false;}
 static bool Target(ref Combat.CombatUnitPower.TargetGroup __result){__result=new Combat.CombatUnitPower.TargetGroup();return false;}
 static bool Schedule(Combat.CombatUnitPower __instance){scheduled=__instance.Power.Power.ID;return false;}
 static bool Skip(){return false;}
 static bool Loop(){loops++;return false;}
 static bool NoAction(){noActions++;return false;}
 static void Hook(Harmony h,MethodInfo target,string prefix){if(target==null)throw new Exception("missing target "+prefix);h.Patch(target,prefix:new HarmonyMethod(typeof(EnemyConditionTests).GetMethod(prefix,F)));}
 static T Blank<T>(){return (T)FormatterServices.GetUninitializedObject(typeof(T));}
 static FieldInfo Field(object obj,string name){for(Type t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,F|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new Exception("missing field "+name);}
 static void Set(object obj,string name,object val){Field(obj,name).SetValue(obj,val);}
 static void Property(object obj,string name,object val){obj.GetType().GetProperty(name,F).GetSetMethod(true).Invoke(obj,new[]{val});}
 static void Init(object obj,SRRWGame g,string id){if(obj is GameElement){Set(obj,"game",g);Set(obj,"id",id);}for(Type t=obj.GetType();t!=null;t=t.BaseType)foreach(var f in t.GetFields(F|BindingFlags.DeclaredOnly)){if(f.FieldType==typeof(ModifiedValue))f.SetValue(obj,new ModifiedValue(0));else if(f.FieldType.IsGenericType&&f.FieldType.GetGenericTypeDefinition()==typeof(GameElementCollection<>))f.SetValue(obj,Activator.CreateInstance(f.FieldType,new object[]{null}));else if(f.FieldType.IsGenericType&&f.FieldType.GetGenericTypeDefinition()==typeof(List<>))f.SetValue(obj,Activator.CreateInstance(f.FieldType));}}
 static void Check(bool v,string why){if(!v)throw new Exception(why);}
 static void Test(string name,Action a){try{a();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
}
