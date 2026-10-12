using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using SRBugfix;

internal static class PredictionTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int passed,failed;
    static int Main(string[] args)
    {
        string managed=Path.Combine(args[0],"Star Renegades_Data/Managed");
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string name=new AssemblyName(e.Name).Name+".dll";string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);if(!File.Exists(p))p=Path.Combine(managed,name);return File.Exists(p)?Assembly.LoadFrom(p):null;};
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        var harmony=new Harmony("test.srbugfix.prediction");ReflectionPreviewFix.Enabled=true;DamageInspectionFix.Enabled=true;
        try
        {
            var target=ReflectionPreviewFix.ResolveTarget();harmony.Patch(target,transpiler:new HarmonyMethod(typeof(ReflectionPreviewFix).GetMethod("Transpile",BindingFlags.Static|BindingFlags.NonPublic)));
            Test("ReflectedArmorAndLaterAttackMatchExecution",()=>{
                var f=new Fixture();f.State.armor=15;Set(f.Attacker,"armorMax",new ModifiedValue(15));Set(f.Attacker,"currentArmor",15);f.State.armorMax=new ModifiedValue(15);Set(f.Power,"armorDamageMin",new ModifiedValue(10));
                f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),4.5f,false,true,false,true);
                Equal(f.State.armor,15,"reflection stripped armor");
                Set(f.Power,"armorDamageMin",new ModifiedValue(0));float before=f.State.health;
                f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),25f,false,false,false,true);Equal(before-f.State.health,10f,"later attack overestimated");
            });
            Test("OrdinaryArmorDamageUnchanged",()=>{
                var f=new Fixture();Set(f.Power,"armorDamageMin",new ModifiedValue(10));f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),10f,false,false,false,true);Equal(f.State.armor,0,"ordinary armor damage changed");
            });
            Test("CritArmorDamageUnchanged",()=>{
                var f=new Fixture();Set(f.Power,"armorDamageMin",new ModifiedValue(3));Set(f.Power,"criticalArmorDamageMin",new ModifiedValue(2));f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),10f,true,false,false,true);Equal(f.State.armor,5,"CRIT armor damage changed");
            });
            Test("DelayForecastMatchesRoundBoundaryExecution",()=>{
                var f=new Fixture();Set(f.Power,"timeDamage",new ModifiedValue(15));Set(f.Power,"timeDamageDefense",new ModifiedValue(75));f.State.hasPowerExecution=true;f.State.currentPower=f.CombatPower;f.State.powerExecutionTime=new Combat.CombatTime(0,57);f.State.currentStagger=1;
                f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),0f,true,false,true,false);Equal(f.State.powerExecutionTime.TotalSeconds(),60,"forecast hid boundary attack");
            });
            Test("ChimeraTwoResistedStaggersStayThisRound",()=>{
                var f=new Fixture();Set(f.Power,"timeDamage",new ModifiedValue(15));Set(f.Power,"timeDamageDefense",new ModifiedValue(50));Set(f.Power,"criticalTargetStaggerChange",-1);Set(f.Attacker,"staggerMax",2);Set(f.Power,"executionCap",120);f.State.hasPowerExecution=true;f.State.currentPower=f.CombatPower;f.State.powerExecutionTime=new Combat.CombatTime(0,45);f.State.currentStagger=2;
                f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),0f,true,false,true,false);f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,15),0f,true,false,true,false);
                Equal(f.State.powerExecutionTime.TotalSeconds(),59,"forecast moved Claw beyond round");Equal(f.State.currentStagger,0,"stagger charges changed");
            });
            Test("DirectInspectionResourceDeltasHaveCorrectSign",()=>{
                var f=new Fixture();Set(f.Attacker,"currentHealth",75);Set(f.Attacker,"maximumHealth",new ModifiedValue(100));Set(f.Attacker,"currentArmor",15);Set(f.Attacker,"currentShield",80);Set(f.Attacker,"shieldCap",new ModifiedValue(80));f.State.health=50;f.State.armor=5;f.State.armorMax=new ModifiedValue(15);f.State.shield=60;f.State.shieldCap=new ModifiedValue(100);
                var result=DamageInspectionFix.SignedResult(f.State);Equal(result.healthDelta,-25f,"damage sign reversed");Equal(result.armorDelta,-10f,"armor maximum used");Equal(result.shieldDelta,-20f,"shield sign reversed");Equal(result.shieldCapDelta,20f,"cap sign reversed");
            });
            Test("InspectionHealingAndZeroChanges",()=>{
                var f=new Fixture();Set(f.Attacker,"currentHealth",50);f.State.health=75;var result=DamageInspectionFix.SignedResult(f.State);Equal(result.healthDelta,25f,"healing sign reversed");f.State.health=50;Equal(DamageInspectionFix.SignedResult(f.State).healthDelta,0f,"zero change became damage");
            });
            Test("UnsupportedEstimateIlRejected",()=>{
                bool rejected=false;try{ReflectionPreviewFix.Transpile(new CodeInstruction[0]).ToArray();}catch(InvalidDataException){rejected=true;}Check(rejected,"unmatched estimate IL accepted");
            });
            Test("InspectionTargetIsOnlyDirectUiCall",()=>{
                var method=DamageInspectionFix.ResolveTarget();Check(method.Name=="ShowDamagePreview"&&method.DeclaringType.FullName=="Mdi.SRRW.UI.CombatController+ScheduleUnitsState+ScheduleUnitsSubstate","wrong inspector target");
                bool rejected=false;try{DamageInspectionFix.Transpile(new CodeInstruction[0]).ToArray();}catch(InvalidDataException){rejected=true;}Check(rejected,"unmatched inspection IL accepted");
            });
            Test("ActualInspectionIlPatchedAndOtherConsumersUntouched",()=>{
                var method=DamageInspectionFix.ResolveTarget();harmony.Patch(method,transpiler:new HarmonyMethod(typeof(DamageInspectionFix).GetMethod("Transpile",BindingFlags.Static|BindingFlags.NonPublic)));
                Check(Harmony.GetPatchInfo(method).Transpilers.Any(p=>p.owner==harmony.Id),"UI hook missing");
                Check(Harmony.GetPatchInfo(typeof(Combat.CombatUnitPower).GetMethod("EstimateResultsAgainstTarget"))==null,"shared estimator patched");
                Check(Harmony.GetPatchInfo(typeof(Combat.CombatUnitPower.EstimatedResults).GetConstructor(new[]{typeof(Combat.CombatUnit.EstimatedState)}))==null,"shared result constructor patched");
            });
            Test("GuardedInstructionEditsPreserveLabelsAndTimeBoost",()=>{
                var original=PatchProcessor.GetOriginalInstructions(target).ToList();var armor=typeof(UnitPower).GetProperty("ArmorDamageMin").GetGetMethod();
                var site=original.Single(c=>c.Calls(armor));var generator=new DynamicMethod("label",typeof(void),Type.EmptyTypes).GetILGenerator();for(int i=0;i<1000;i++)generator.DefineLabel();var label=generator.DefineLabel();site.labels.Add(label);
                site.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                var patched=ReflectionPreviewFix.Transpile(original).ToList();var load=patched.Single(c=>c.labels.Contains(label));
                Check(load.opcode==OpCodes.Ldarg_S&&load.blocks.Count==1,"branch/exception entry moved past inserted argument");
                var round=typeof(UnityEngine.Mathf).GetMethod("RoundToInt",new[]{typeof(float)});
                Equal(patched.Count(c=>c.Calls(round)),original.Count(c=>c.Calls(round))-1,"unrelated rounding changed");
                Check(original.Single(c=>c.Calls(armor)).labels.Contains(label),"transpiler mutated input");
            });
            Test("DuplicateInspectionCallRejected",()=>{
                var call=typeof(Combat.CombatUnitPower).GetMethod("EstimateResultsAgainstTarget");bool rejected=false;
                try{DamageInspectionFix.Transpile(new[]{new CodeInstruction(OpCodes.Callvirt,call),new CodeInstruction(OpCodes.Callvirt,call)}).ToArray();}catch(InvalidDataException){rejected=true;}Check(rejected,"ambiguous UI call accepted");
            });
            Test("ReflectionShieldsMultipleHitsAndSourcePowerPreserved",()=>{
                var f=new Fixture();Set(f.Power,"armorDamageMin",new ModifiedValue(10));f.State.shield=20;f.State.shieldCap=new ModifiedValue(20);
                for(int i=0;i<2;i++)f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),4.5f,false,true,false,true);
                Equal(f.State.armor,10,"reflected multihit stripped armor");Equal(f.State.health,100,"reflected shielded hit reached health");Equal(f.Power.ArmorDamageMin,10,"source power mutated");
            });
            Test("DelayCapChargeAndFullResistancePreserved",()=>{
                foreach(int defense in new[]{0,50,100})foreach(int charges in new[]{0,1}){
                    var f=new Fixture();Set(f.Power,"timeDamage",new ModifiedValue(15));Set(f.Power,"timeDamageDefense",new ModifiedValue(defense));Set(f.Power,"executionCap",60);
                    f.State.hasPowerExecution=true;f.State.currentPower=f.CombatPower;f.State.powerExecutionTime=new Combat.CombatTime(0,57);f.State.currentStagger=charges;
                    f.State.EstimateDamage(f.State,f.CombatPower,new Combat.CombatTime(0,0),0,false,false,false,false);
                    Equal(f.State.powerExecutionTime.TotalSeconds(),charges==0?57:57+Math.Min(3,(int)(15f*(1f-defense*.01f))),"cap/resistance/charges changed");
                }
            });
            Test("DisabledHelpersUseOriginalBehavior",()=>{
                var f=new Fixture();Set(f.Power,"armorDamageMin",new ModifiedValue(10));ReflectionPreviewFix.Enabled=false;
                try{Equal(ReflectionPreviewFix.ArmorDamageForEstimate(f.Power,true),10,"disabled reflect changed");Equal(ReflectionPreviewFix.DelayForEstimate(7.5f),8,"disabled delay changed");}finally{ReflectionPreviewFix.Enabled=true;}
            });
        }
        finally{harmony.UnpatchAll(harmony.Id);ReflectionPreviewFix.Enabled=false;DamageInspectionFix.Enabled=false;}
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    internal sealed class Fixture
    {
        public Unit Attacker;public UnitPower Power;public Combat.CombatUnit Unit;public Combat.CombatUnitPower CombatPower;public Combat.CombatUnit.EstimatedState State;
        public Fixture()
        {
            var game=Blank<SRRWGame>();Set(game,"currentDifficulty",new DifficultyData{id="default"});
            Attacker=Blank<Unit>();Init(Attacker,game,"unit");Set(Attacker,"currentHealth",100);Set(Attacker,"currentArmor",10);Set(Attacker,"maximumHealth",new ModifiedValue(100));Set(Attacker,"armorMax",new ModifiedValue(10));
            Unit=Blank<Combat.CombatUnit>();Init(Unit,game,"unit");Set(Unit,"unit",Attacker);
            Power=Blank<UnitPower>();Init(Power,game,"power");Set(Power,"unit",Attacker);
            CombatPower=Blank<Combat.CombatUnitPower>();Init(CombatPower,game,"power");Set(CombatPower,"unit",Unit);Set(CombatPower,"power",Power);
            var combat=Blank<Combat>();Init(combat,game,"combat");Set(Unit,"combat",combat);
            var execution=Blank<Combat.CombatPowerExecution>();Init(execution,game,"execution");Set(execution,"<power>k__BackingField",CombatPower);Set(Unit,"currentPowerExecution",execution);
            State=Blank<Combat.CombatUnit.EstimatedState>();Set(State,"unit",Unit);State.armor=10;State.health=100;State.healthDefense=new ModifiedValue(0);State.armorDefense=new ModifiedValue(0);State.shieldDefense=new ModifiedValue(0);State.armorMax=new ModifiedValue(10);State.shieldCap=new ModifiedValue(0);State.exploitedVulnerabilities=new List<Vulnerability>();
        }
    }
    static T Blank<T>(){return (T)FormatterServices.GetUninitializedObject(typeof(T));}
    static void Init(object obj,SRRWGame game,string id)
    {
        if(obj is GameElement){Set(obj,"game",game);Set(obj,"id",id);}
        for(Type type=obj.GetType();type!=null;type=type.BaseType)foreach(var field in type.GetFields(Flags|BindingFlags.DeclaredOnly))
        {
            if(field.FieldType==typeof(ModifiedValue))field.SetValue(obj,new ModifiedValue(0));
            if(field.FieldType.IsGenericType&&field.FieldType.GetGenericTypeDefinition()==typeof(GameElementCollection<>))field.SetValue(obj,Activator.CreateInstance(field.FieldType,new object[]{null}));
        }
    }
    internal static void Set(object obj,string name,object value){for(Type type=obj.GetType();type!=null;type=type.BaseType){var field=type.GetField(name,Flags|BindingFlags.DeclaredOnly);if(field!=null){field.SetValue(obj,value);return;}}throw new Exception("missing fixture field "+name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Equal(object value,object expected,string message){Check(Convert.ToDouble(value)==Convert.ToDouble(expected),message+": expected "+expected+", got "+value);}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
