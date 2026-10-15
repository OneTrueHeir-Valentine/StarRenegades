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

internal static class ResourceForecastTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int passed,failed;
    static int Main(string[] args)
    {
        string managed=Path.Combine(args[0],"Star Renegades_Data/Managed");
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,new AssemblyName(e.Name).Name+".dll");if(!File.Exists(p))p=Path.Combine(managed,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        var harmony=new Harmony("test.srbugfix.resources");ReflectionPreviewFix.Enabled=true;ProjectedEffectFix.Enabled=true;DamageInspectionFix.Enabled=true;
        harmony.Patch(ReflectionPreviewFix.ResolveTarget(),transpiler:Hook(typeof(ReflectionPreviewFix),"Transpile"));ProjectedEffectFix.Install(harmony);ReflectionPreviewFix.InstallReactionHooks(harmony);
#if RESOURCE_FIX
        ResourceForecastFix.Enabled=true;ResourceForecastFix.Install(harmony);
        EffectLifecycleFix.Enabled=true;EffectLifecycleFix.Install(harmony);
#endif
        try {
            Test("SelfHealingCapsAtMaximum",()=>{new Fixture().Heal(170,25,180,180);});
            Test("CappedHealingThenLethalDamageMatchesNative",()=>{new Fixture().HealThenDamage();});
            Test("AllyHealingCapsAtMaximum",()=>{new Fixture().Heal(190,100,200,200);});
            Test("ZeroHealingDoesNotClampUnrelatedHealth",()=>{var f=new Fixture();f.TargetState.health=125;f.TargetState.EstimateDamage(f.SourceState,f.CombatPower,new Combat.CombatTime(0,0),0,false,false,false,false);Equal(f.TargetState.health,125,"zero heal changed health");});
            Test("HealingBelowMaximumPreserved",()=>{new Fixture().Heal(50,25,75);});
            Test("ExistingTemporaryArmorProtectsWithoutBaseGain",()=>{new Fixture().SpyArmor(0,0,new[]{95f});});
            Test("TemporaryArmorConsumedBeforeBaseAcrossHits",()=>{new Fixture().SpyArmor(0,2,new[]{93f,74f,54f});});
            Test("TemporaryArmorStripThenReflectionMatchesNative",()=>{new Fixture().SpyArmor(0,2,new[]{93f,74f},true);});
            Test("TemporaryArmorGrantCappedByMissingArmor",()=>{new Fixture().SpyArmor(3,0,new[]{95f});});
            Test("ExistingSignedStacksKeepNativeArmorAccounting",()=>{new Fixture().SignedSpyStacks();});
            Test("ProjectedTemporaryArmorHasNoPhantomBaseDelta",()=>{new Fixture().ProjectedSpy(false);});
            Test("ModeARemovesExistingTemporaryArmorInForecastOnly",()=>{new Fixture().CancelLiveSpyArmor();});
            Test("ModeARemovesOnlyProjectedTemporaryArmor",()=>{new Fixture().ProjectedSpy(true);});
            Test("OrdinaryArmorStripPreserved",()=>{new Fixture().CheckHits(45,15,0,5,0,0,0,new[]{65f,25f,0f},new[]{10f,5f,0f},new[]{0f,0f,0f});});
            Test("PerHitRoundingPreserved",()=>{new Fixture().CheckHits(4.5f,0,0,0,0,0,0,new[]{96f,92f,88f},new[]{0f,0f,0f},new[]{0f,0f,0f});});
#if RESOURCE_FIX
            Test("ResourcePreflightDoesNotPatch",()=>{int before=Harmony.GetAllPatchedMethods().Count();ResourceForecastFix.ValidateTargets();Equal(Harmony.GetAllPatchedMethods().Count(),before,"preflight installed hooks");});
            Test("ResourceUnknownDamageIlRejected",()=>{bool rejected=false;try{ResourceForecastFix.TranspileDamage(new CodeInstruction[0]).ToArray();}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("unknown damage shape accepted");});
            Test("ResourceUnknownPowerIlRejected",()=>{bool rejected=false;try{ResourceForecastFix.TranspilePower(new CodeInstruction[0]).ToArray();}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("unknown power shape accepted");});
            Test("DisabledResourcesKeepNativeBehavior",()=>{ResourceForecastFix.Enabled=false;try{var f=new Fixture();f.Heal(170,25,180,180,195);}finally{ResourceForecastFix.Enabled=true;}});
#endif
        } finally {harmony.UnpatchAll(harmony.Id);ReflectionPreviewFix.Enabled=false;ProjectedEffectFix.Enabled=false;DamageInspectionFix.Enabled=false;
#if RESOURCE_FIX
        ResourceForecastFix.Enabled=false;
        EffectLifecycleFix.Enabled=false;
#endif
        }
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    sealed class Fixture
    {
        public SRRWGame Game;public Combat Combat;public Combat.CombatUnit Source,Target;public UnitPower Power;public Combat.CombatUnitPower CombatPower;public Combat.CombatUnit.EstimatedState SourceState,TargetState;
        public Fixture()
        {
            Game=Blank<SRRWGame>();Init(Game,Game,"game");Set(Game,"currentDifficulty",new DifficultyData{id="default"});
            var gd=new GameData();Set(gd,"<GameRulesData>k__BackingField",new GameRulesData());Set(Game,"gameData",gd);
            Combat=Blank<Combat>();Init(Combat,Game,"combat");
            var team=Blank<Combat.CombatTeam>();Init(team,Game,"sourceTeam");Set(Combat,"playerTeam",team);
            Source=MakeUnit("source");Target=MakeUnit("target");Set(Source,"team",team);var targetTeam=Blank<Combat.CombatTeam>();Init(targetTeam,Game,"targetTeam");Set(Target,"team",targetTeam);
            Power=Blank<UnitPower>();Init(Power,Game,"power");Set(Power,"unit",Source.Unit);Set(Power,"noFirstStrike",true);Set(Power,"hostility",Mdi.SRRW.Engine.Power.PowerHostility.Enemy);
            var power=Blank<Power>();Init(power,Game,"power");Set(power,"powerData",new PowerData());Set(Power,"power",power);
            CombatPower=Blank<Combat.CombatUnitPower>();Init(CombatPower,Game,"power");Set(CombatPower,"unit",Source);Set(CombatPower,"power",Power);
            SourceState=new Combat.CombatUnit.EstimatedState(Source);SourceState.powerExecutionTime=new Combat.CombatTime(0,0);ResetTarget();
        }
        Combat.CombatUnit MakeUnit(string id)
        {
            var u=Blank<Unit>();Init(u,Game,id);Set(u,"OnCurrentHealthChanged",new Action<Unit,float,float>((x,a,b)=>{}));Set(u,"currentHealth",100);Set(u,"maximumHealth",new ModifiedValue(100));Set(u,"damage",new ModifiedValue(100));
            var cu=Blank<Combat.CombatUnit>();Init(cu,Game,id);Set(cu,"unit",u);Set(cu,"combat",Combat);return cu;
        }
        public void ResetTarget(){TargetState=new Combat.CombatUnit.EstimatedState(Target);}
        public Effect Effect(string id,float criticalDefense=0,string tag=null)
        {
            var e=Blank<Effect>();Init(e,Game,id);var data=new EffectData{id=id,tags=tag==null?new string[0]:new[]{tag}};((UnityEngine.ISerializationCallbackReceiver)data).OnAfterDeserialize();Set(e,"effectData",data);
            var mod=Blank<UnitModifier>();Init(mod,Game,id+"mod");Set(mod,"modifierData",new UnitModifierData{tags=new string[0],modCriticalDefenseAbsolute=criticalDefense});Set(e,"unitModifiers",new List<UnitModifier>{mod});return e;
        }
        Combat.ExecutePowerEvent nativeEvent;Type actualType;IDictionary actualStates;
        void NativeState()
        {
            if(nativeEvent!=null)return;
            nativeEvent=Blank<Combat.ExecutePowerEvent>();Init(nativeEvent,Game,"event");Set(nativeEvent,"combat",Combat);var nativeExecution=Blank<Combat.CombatPowerExecution>();Init(nativeExecution,Game,"execution");Set(nativeExecution,"<power>k__BackingField",CombatPower);Set(nativeEvent,"<powerExecution>k__BackingField",nativeExecution);actualType=typeof(Combat.ExecutePowerEvent).GetNestedType("ActualUnitState",BindingFlags.NonPublic);
            actualStates=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string),actualType));
            foreach(var unit in new[]{Source,Target})actualStates.Add(unit.ID,Activator.CreateInstance(actualType,Flags,null,new object[]{Combat,unit,Source},null));Set(nativeEvent,"actualUnitState",actualStates);
        }
        public float NativeDamage(Combat.CombatUnit target,float sourceDamage,bool critical,float criticalDamage,float armorDamage,out float reflected)
        {
            NativeState();object[] args={new List<Combat.CombatPowerResult>(),Power,target,sourceDamage,critical,criticalDamage,0f,armorDamage,false,0f,0f,0f,0f,0f,0f,false,false,new Dictionary<string,float>(),new List<Effect>(),new List<Vulnerability>(),0f,0f};
            float damage=(float)typeof(Combat.ExecutePowerEvent).GetMethod("CalculateDamageAgainstTarget",Flags).Invoke(nativeEvent,args);reflected=(float)args[9];return damage;
        }
        public float NativeApplyDamage(Combat.CombatUnit target,float damage)
        {
            typeof(Combat.ExecutePowerEvent).GetMethod("ApplyDamage",Flags).Invoke(nativeEvent,new object[]{target,damage,new Dictionary<string,float>()});return (float)actualType.GetProperty("currentHealth").GetValue(actualStates[target.ID],null);
        }

        public void CheckHits(float damage,float armor,float shield,float armorDamage,float piercing,float shieldDef,float healthDef,float[] wantH,float[] wantA,float[] wantS)
        {
            Set(Target.Unit,"currentArmor",(int)armor);Set(Target.Unit,"armorMax",new ModifiedValue(armor));Set(Target.Unit,"currentShield",(int)shield);Set(Target.Unit,"shieldCap",new ModifiedValue(shield));Set(Target.Unit,"shieldDefense",new ModifiedValue(shieldDef));Set(Target.Unit,"healthDefense",new ModifiedValue(healthDef));
            Set(Power,"armorDamageMin",new ModifiedValue(armorDamage));Set(Power,"armorPiercingPercent",piercing);ResetTarget();
            for(int i=0;i<wantH.Length;i++){
                NativeState();object[] args={new List<Combat.CombatPowerResult>(),Power,Target,damage,false,0f,0f,armorDamage,false,0f,0f,0f,0f,0f,0f,false,false,new Dictionary<string,float>(),new List<Effect>(),new List<Vulnerability>(),0f,0f};
                float d=(float)typeof(Combat.ExecutePowerEvent).GetMethod("CalculateDamageAgainstTarget",Flags).Invoke(nativeEvent,args);
                typeof(Combat.ExecutePowerEvent).GetMethod("ApplyShieldDamage",Flags).Invoke(nativeEvent,new[]{(object)Target,args[12]});
                float nh=NativeApplyDamage(Target,d),na=ReadNative("currentArmor"),ns=ReadNative("currentShield");
                TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,0),damage,false,false,false,false);
                Console.WriteLine("DATA hit="+(i+1)+" damage="+damage+" shieldDefense="+shieldDef+" native="+nh+","+na+","+ns+" forecast="+TargetState.health+","+TargetState.armor+","+TargetState.shield+" expected="+wantH[i]+","+wantA[i]+","+wantS[i]);
                Equal(nh,wantH[i],"native health");Equal(na,wantA[i],"native armor");Equal(ns,wantS[i],"native shield");Equal(TargetState.health,wantH[i],"forecast health");Equal(TargetState.armor,wantA[i],"forecast armor");Equal(TargetState.shield,wantS[i],"forecast shield");
            }
        }
        Effect SpyEffect()
        {
            var e=Effect("eArtModeB");e.EffectData.armorGainAbsolute=10;e.EffectData.isStackable=true;e.EffectData.ignoreArmor=true;e.EffectData.ignoreShield=true;return e;
        }
        public void SpyArmor(int baseArmor,float strip,float[] wantH,bool reflectAfter=false)
        {
            Set(Target.Unit,"currentHealth",110);Set(Target.Unit,"maximumHealth",new ModifiedValue(110));Set(Target.Unit,"currentArmor",baseArmor);Set(Target.Unit,"armorMax",new ModifiedValue(5));
            var e=SpyEffect();var ue=new UnitEffect(Target.Unit,e);Target.Unit.Effects.Add(ue);Target.Unit.ArmorGainingEffects.Add(ue);
            var ce=new Combat.CombatUnitEffect(Combat,Target,ue,Target,false);((GameElementCollection<Combat.CombatUnitEffect>)typeof(Combat.CombatUnit).GetField("effects",Flags).GetValue(Target)).Add(ce);
            var callback=(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,float>)Delegate.CreateDelegate(typeof(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,float>),ce,typeof(Combat.CombatUnitEffect).GetMethod("OnEffectArmorDamageApplied",Flags));Set(Combat,"OnEffectArmorDamageApplied",callback);
            Set(Power,"armorDamageMin",new ModifiedValue(strip));ResetTarget();NativeState();float firstArmor=ce.CurrentArmor;
            for(int i=0;i<wantH.Length;i++){
                float reflected;float damage=NativeDamage(Target,20,false,0,strip,out reflected);float nh=NativeApplyDamage(Target,damage);float liveEffectArmor=ce.CurrentArmor;
                TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,0),20,false,false,false,false);
                Console.WriteLine("DATA spy hit="+(i+1)+" nativeHealth="+nh+" forecastHealth="+TargetState.health+" nativeEffectArmor="+ce.CurrentArmor+" baseArmor="+TargetState.armor);
                Equal(nh,wantH[i],"native temporary armor");Equal(TargetState.health,wantH[i],"forecast temporary armor");Equal(ce.CurrentArmor,liveEffectArmor,"forecast changed live temporary armor");
            }
            if(reflectAfter){
                Set(Power,"armorDamageMin",new ModifiedValue(10));float reflected;float damage=NativeDamage(Target,20,false,0,0,out reflected);float nh=NativeApplyDamage(Target,damage);float liveEffectArmor=ce.CurrentArmor;
                TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,0),20,false,true,false,false);
                Equal(nh,55,"native reflected damage after strip");Equal(TargetState.health,55,"forecast reflected damage after strip");Equal(ce.CurrentArmor,liveEffectArmor,"reflection changed live temporary armor");Equal(Power.ArmorDamageMin,10,"reflection changed source power");
            }
            Equal(firstArmor,5-baseArmor,"native armor cap");Equal(Target.Unit.CurrentArmor,baseArmor,"forecast mutated live base armor");
            Equal(Target.Unit.CurrentHealth,110,"forecast changed live target health");Equal(Source.Unit.CurrentHealth,100,"forecast changed live source health");
            if(strip==0)Equal(DamageInspectionFix.SignedResult(TargetState).armorDelta,0,"phantom base armor delta");
        }
        public void SignedSpyStacks()
        {
            Set(Target.Unit,"currentHealth",110);Set(Target.Unit,"maximumHealth",new ModifiedValue(110));Set(Target.Unit,"currentArmor",3);Set(Target.Unit,"armorMax",new ModifiedValue(5));
            var e=SpyEffect();var effects=(GameElementCollection<Combat.CombatUnitEffect>)typeof(Combat.CombatUnit).GetField("effects",Flags).GetValue(Target);
            var a=new UnitEffect(Target.Unit,e);Set(a,"id","armor-a");Target.Unit.Effects.Add(a);Target.Unit.ArmorGainingEffects.Add(a);var first=new Combat.CombatUnitEffect(Combat,Target,a,Target,false);Set(first,"currentArmor",1f);effects.Add(first);
            var b=new UnitEffect(Target.Unit,e);Set(b,"id","armor-b");Target.Unit.Effects.Add(b);Target.Unit.ArmorGainingEffects.Add(b);var second=new Combat.CombatUnitEffect(Combat,Target,b,Target,false);effects.Add(second);
            var cb=(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,float>)Delegate.CreateDelegate(typeof(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,float>),first,typeof(Combat.CombatUnitEffect).GetMethod("OnEffectArmorDamageApplied",Flags));
            cb+=(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,float>)Delegate.CreateDelegate(typeof(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,float>),second,typeof(Combat.CombatUnitEffect).GetMethod("OnEffectArmorDamageApplied",Flags));Set(Combat,"OnEffectArmorDamageApplied",cb);
            NativeState();float reflected;float damage=NativeDamage(Target,20,false,0,2,out reflected);Equal(NativeApplyDamage(Target,damage),93,"native stack preparation health");Equal(first.CurrentArmor,-1,"native first signed stack");Equal(second.CurrentArmor,1,"native second signed stack");
            Set(Target.Unit,"currentHealth",93);Set(Power,"armorDamageMin",new ModifiedValue(1));ResetTarget();damage=NativeDamage(Target,20,false,0,1,out reflected);float nh=NativeApplyDamage(Target,damage);
            TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,0),20,false,false,false,false);
            Console.WriteLine("DATA signed stacks nativeHealth="+nh+" forecastHealth="+TargetState.health+" nativeBase="+ReadNative("currentArmor")+" forecastBase="+TargetState.armor);
            Equal(nh,75,"native signed stack health");Equal(ReadNative("currentArmor"),2,"native signed stack base armor");Equal(TargetState.health,75,"forecast signed stack health");Equal(TargetState.armor,2,"forecast signed stack base armor");Equal(Target.Unit.CurrentArmor,3,"forecast changed live armor");
        }
        public void CancelLiveSpyArmor()
        {
            Set(Target.Unit,"currentHealth",110);Set(Target.Unit,"maximumHealth",new ModifiedValue(110));Set(Target.Unit,"armorMax",new ModifiedValue(5));
            var e=SpyEffect();var ue=new UnitEffect(Target.Unit,e);Target.Unit.Effects.Add(ue);Target.Unit.ArmorGainingEffects.Add(ue);
            var ce=new Combat.CombatUnitEffect(Combat,Target,ue,Target,false);((GameElementCollection<Combat.CombatUnitEffect>)typeof(Combat.CombatUnit).GetField("effects",Flags).GetValue(Target)).Add(ce);ResetTarget();
            Power.EffectsCancelled.Add(e);Set(Power,"hostility",Mdi.SRRW.Engine.Power.PowerHostility.Ally);bool crit;
            TargetState.EstimatePower(SourceState,CombatPower,new Combat.CombatTime(0,0),false,false,out crit,false,false);Power.EffectsCancelled.Clear();
            Set(Power,"hostility",Mdi.SRRW.Engine.Power.PowerHostility.Enemy);TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,1),20,false,false,false,false);
            Equal(TargetState.health,90,"existing armor survived forecast cancellation");Equal(TargetState.armor,0,"cancellation changed base armor");Equal(ce.CurrentArmor,5,"cancellation changed live temporary armor");Equal(Target.Unit.Effects.Count,1,"forecast removed live effect");Equal(Target.Unit.CurrentHealth,110,"forecast changed live health");
        }
        public void ProjectedSpy(bool remove)
        {
            Set(Target.Unit,"currentHealth",110);Set(Target.Unit,"maximumHealth",new ModifiedValue(110));Set(Target.Unit,"armorMax",new ModifiedValue(5));ResetTarget();
            var e=SpyEffect();Power.EffectsApplied.Add(e);Set(Power,"hostility",Mdi.SRRW.Engine.Power.PowerHostility.Ally);bool crit;
            TargetState.EstimatePower(SourceState,CombatPower,new Combat.CombatTime(0,0),false,false,out crit,false,false);Power.EffectsApplied.Clear();
            Equal(DamageInspectionFix.SignedResult(TargetState).armorDelta,0,"temporary grant changed base armor delta");
            if(remove){Power.EffectsCancelled.Add(e);TargetState.EstimatePower(SourceState,CombatPower,new Combat.CombatTime(0,1),false,false,out crit,false,false);Power.EffectsCancelled.Clear();}
            Set(Power,"hostility",Mdi.SRRW.Engine.Power.PowerHostility.Enemy);TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,2),20,false,false,false,false);
            Equal(TargetState.health,remove?90:95,"projected mode armor");Equal(Target.Unit.Effects.Count,0,"projection changed live effects");Equal(Target.Unit.CurrentArmor,0,"projection changed live armor");
        }
        float ReadNative(string property){return (float)actualType.GetProperty(property).GetValue(actualStates[Target.ID],null);}
        public void HealThenDamage()
        {
            Heal(170,25,180,180,null,false);nativeEvent=null;Set(Power,"heal",new ModifiedValue(0));float reflected;
            float damage=NativeDamage(Target,185,false,0,0,out reflected);float nh=NativeApplyDamage(Target,damage);
            TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,1),185,false,false,false,false);
            Console.WriteLine("DATA capped heal then185 nativeHealth="+nh+" forecastHealth="+TargetState.health);
            Equal(nh,0,"native lethal hit after capped healing");Equal(TargetState.health,0,"forecast lethal hit after capped healing");Equal(Target.Unit.CurrentHealth,180,"forecast changed live health after healing");Equal(Source.Unit.CurrentHealth,100,"forecast changed live source health");
        }
        public void Heal(float before,float amount,float want,float maximum=100,float? forecastWant=null,bool verifyForecast=true)
        {
            Set(Target.Unit,"maximumHealth",new ModifiedValue(maximum));if(amount==25){Set(Power,"unit",Target.Unit);Set(CombatPower,"unit",Target);}Set(Target.Unit,"currentHealth",(int)before);Set(Power,"heal",new ModifiedValue(amount));Set(Power,"hostility",Mdi.SRRW.Engine.Power.PowerHostility.Ally);ResetTarget();NativeState();
            Set(nativeEvent,"combat",Combat);
            var execution=Blank<Combat.CombatPowerExecution>();Init(execution,Game,"execution");Set(execution,"<power>k__BackingField",CombatPower);Set(nativeEvent,"<powerExecution>k__BackingField",execution);
            var typ=typeof(Combat.ExecutePowerEvent);object apps=Activator.CreateInstance(typeof(List<>).MakeGenericType(typ.GetNestedType("EffectApplication",BindingFlags.NonPublic)));object vulns=Activator.CreateInstance(typeof(List<>).MakeGenericType(typ.GetNestedType("ExploitedVulnerabilities",BindingFlags.NonPublic)));
            var results=new List<Combat.CombatPowerResult>();
            object[] args={execution,new List<Combat.CombatUnit>{Target},results,apps,apps,vulns,new List<Combat.CombatUnit>(),new Dictionary<string,int>(),Target,Power,0f,0f,0f,0f,0f,false,false};
            typ.GetMethod("CalculateAgainstTarget",Flags).Invoke(nativeEvent,args);
            var heal=results.Single(r=>r.type==Combat.CombatPowerResult.Type.Heal);typeof(Combat.CombatUnit).GetMethod("OnHealed",Flags).Invoke(Target,new object[]{Target,heal.healAmount,Source});
            bool crit;TargetState.EstimatePower(amount==25?TargetState:SourceState,CombatPower,new Combat.CombatTime(0,0),false,false,out crit,false,false);
            Console.WriteLine("DATA heal before="+before+" amount="+amount+" nativeAmount="+heal.healAmount+" nativeHealth="+Target.Unit.CurrentHealth+" forecastHealth="+TargetState.health+" expected="+want);
            Equal(Target.Unit.CurrentHealth,want,"native heal");if(verifyForecast)Equal(TargetState.health,forecastWant??want,"forecast heal");
        }
    }
    static T Blank<T>(){return (T)FormatterServices.GetUninitializedObject(typeof(T));}
    static void Init(object obj,SRRWGame game,string id)
    {
        if(obj is GameElement){Set(obj,"game",game);Set(obj,"id",id);}
        for(Type t=obj.GetType();t!=null;t=t.BaseType)foreach(var f in t.GetFields(Flags|BindingFlags.DeclaredOnly))
        {
            if(f.FieldType==typeof(ModifiedValue))f.SetValue(obj,new ModifiedValue(0));
            else if(f.FieldType.IsGenericType){var def=f.FieldType.GetGenericTypeDefinition();if(def==typeof(GameElementCollection<>))f.SetValue(obj,Activator.CreateInstance(f.FieldType,new object[]{null}));else if(def==typeof(HashSet<>)||def==typeof(Dictionary<,>)||def==typeof(List<>))f.SetValue(obj,Activator.CreateInstance(f.FieldType));}
        }
    }
    static void Set(object obj,string name,object value){for(Type t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(obj,value);return;}}throw new Exception("missing field "+name);}
    static HarmonyMethod Hook(Type type,string name){return new HarmonyMethod(type.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic));}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;Console.WriteLine("FAIL "+name+": "+e);}}
    static void Equal(object actual,object expected,string message){if(Math.Abs(Convert.ToDouble(actual)-Convert.ToDouble(expected))>.0001)throw new Exception(message+": expected "+expected+", got "+actual);}
}
