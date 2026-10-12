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

internal static class ForecastFollowupTests
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
        var harmony=new Harmony("test.srbugfix.forecast-followup");ReflectionPreviewFix.Enabled=true;
        harmony.Patch(ReflectionPreviewFix.ResolveTarget(),transpiler:Hook(typeof(ReflectionPreviewFix),"Transpile"));
#if FOLLOWUP_FIX
        ProjectedEffectFix.Enabled=true;ProjectedEffectFix.Install(harmony);ReflectionPreviewFix.InstallReactionHooks(harmony);
#endif
        try
        {
            Test("ProjectedMarkedBoostsLaterCriticalCombo",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.Hit(60,true);Equal(f.TargetState.health,25,"60 combo with projected 25% vulnerability");Equal(f.Target.Unit.Effects.Count,0,"live effects mutated");});
            Test("ExistingMarkedRetainsVulnerability",()=>{var f=new Fixture();var e=f.Effect("Marked");f.AddLive(e);f.Vulnerability(e);f.ResetTarget();f.Hit(60,true);Equal(f.TargetState.health,25,"existing vulnerability");});
            Test("ProjectedCriticalDefenseChangesBonus",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);Set(f.Power,"criticalDamageBonusPercent",50f);f.TargetState.ApplyEffect(e,f.Source);f.Hit(40,true);Equal(f.TargetState.health,35,"40 + 25 CRIT bonus");Equal(f.Target.Unit.CriticalDefense,0,"live CRIT defense changed");});
            Test("ProjectedTagVulnerabilityBoostsLaterHit",()=>{var f=new Fixture();var e=f.Effect("Marked",0,"marked");f.Vulnerability(null,"marked");f.TargetState.ApplyEffect(e,f.Source);f.Hit(60,true);Equal(f.TargetState.health,25,"effect tag ignored");});
            Test("NormalHitDoesNotUseCriticalVulnerability",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.Hit(60,false);Equal(f.TargetState.health,40,"normal hit changed");});
            Test("NonStackableEffectDoesNotDuplicateCriticalDefense",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);Set(f.Power,"criticalDamageBonusPercent",50f);f.TargetState.ApplyEffect(e,f.Source);f.TargetState.ApplyEffect(e,f.Source);f.Hit(40,true);Equal(f.TargetState.health,35,"nonstackable CRIT modifiers duplicated");});
            Test("StackableEffectRetainsBothCriticalModifiers",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);e.EffectData.isStackable=true;Set(f.Power,"criticalDamageBonusPercent",50f);f.TargetState.ApplyEffect(e,f.Source);f.TargetState.ApplyEffect(e,f.Source);f.Hit(40,true);Equal(f.TargetState.health,30,"stackable modifiers missing");});
            Test("ExplicitCancellationRemovesProjectedVulnerability",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.Power.EffectsCancelled.Add(e);f.EstimatePower(0,false);f.Power.EffectsCancelled.Clear();f.Hit(60,true);Equal(f.TargetState.health,40,"cancelled projected effect retained");});
            Test("TagCancellationRemovesLiveEffectOnlyInForecast",()=>{var f=new Fixture();var e=f.Effect("Marked",-25,"marked");f.AddLive(e);Set(f.Target.Unit,"criticalDefense",new ModifiedValue(-25));f.ResetTarget();f.Power.CancelledEffectTags.Add("marked");f.EstimatePower(0,false);f.Power.CancelledEffectTags.Clear();Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,40,"removed CRIT modifier retained");Equal(f.Target.Unit.Effects.Count,1,"live cancellation performed");Equal(f.Target.Unit.CriticalDefense,-25,"live modifier removed");});
            Test("ModifierUnitTagRequirementPreserved",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);e.UnitModifiers[0].ModifierData.tags=new[]{"required"};Set(f.Power,"criticalDamageBonusPercent",50f);f.TargetState.ApplyEffect(e,f.Source);f.Hit(40,true);Equal(f.TargetState.health,40,"ineligible modifier applied");});
            Test("ProjectedStateDoesNotLeakBetweenScenarios",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.ResetTarget();f.Hit(60,true);Equal(f.TargetState.health,40,"projected effects leaked");});
            Test("ProjectedApplicationRespectsEffectImmunity",()=>{var f=new Fixture();var e=f.Effect("Marked");((GameElementCollection<Effect>)typeof(Unit).GetField("effectImmunities",Flags).GetValue(f.Target.Unit)).Add(e);f.Vulnerability(e);f.Power.EffectsApplied.Add(e);f.EstimatePower(10,false);f.Power.EffectsApplied.Clear();f.Hit(60,true);Equal(f.TargetState.health,30,"immune effect projected");});
            Test("ArmorBlockedEffectDoesNotBoostLaterCombo",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.armor=20;f.TargetState.armorMax=new ModifiedValue(20);f.Power.EffectsApplied.Add(e);f.EstimatePower(10,false);f.Power.EffectsApplied.Clear();f.TargetState.armor=0;f.Hit(60,true);Equal(f.TargetState.health,40,"armor-blocked effect projected");});
            Test("ShieldBlockedEffectDoesNotBoostLaterCombo",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.shield=20;f.TargetState.shieldCap=new ModifiedValue(20);f.Power.EffectsApplied.Add(e);f.EstimatePower(10,false);f.Power.EffectsApplied.Clear();f.TargetState.shield=0;f.Hit(60,true);Equal(f.TargetState.health,40,"shield-blocked effect projected");});
            Test("IgnoringShieldAndArmorAllowsProjectedEffect",()=>{var f=new Fixture();var e=f.Effect("Marked");e.EffectData.ignoreShield=true;e.EffectData.ignoreArmor=true;f.Vulnerability(e);f.TargetState.armor=20;f.TargetState.armorMax=new ModifiedValue(20);f.TargetState.shield=20;f.TargetState.shieldCap=new ModifiedValue(20);f.Power.EffectsApplied.Add(e);f.EstimatePower(10,false);f.Power.EffectsApplied.Clear();f.TargetState.armor=0;f.TargetState.shield=0;f.Hit(60,true);Equal(f.TargetState.health,25,"ignoring barriers effect missing");});
            Test("AllyEffectApplicationDoesNotRequireDamage",()=>{var f=new Fixture();var e=f.Effect("Marked");Set(f.Power,"hostility",Power.PowerHostility.Ally);f.Vulnerability(e);f.Power.EffectsApplied.Add(e);f.EstimatePower(0,false);f.Power.EffectsApplied.Clear();f.Hit(60,true);Equal(f.TargetState.health,25,"ally effect rejected");});
            Test("CancellationsStillApplyWhenShieldBlocksNewEffect",()=>{var f=new Fixture();var e=f.Effect("Marked");f.AddLive(e);f.ResetTarget();f.Vulnerability(e);f.TargetState.shield=20;f.TargetState.shieldCap=new ModifiedValue(20);f.Power.EffectsCancelled.Add(e);f.EstimatePower(10,false);f.Power.EffectsCancelled.Clear();f.TargetState.shield=0;f.Hit(60,true);Equal(f.TargetState.health,40,"blocked hit skipped explicit cancellation");Equal(f.Target.Unit.Effects.Count,1,"live effect removed");});
            Test("StrongerUnstackableEffectRejectsWeakerProjection",()=>{var f=new Fixture();var strong=f.Effect("Strong",-25);strong.EffectData.unstackableGroup="group";strong.EffectData.unstackablePriority=2;var weak=f.Effect("Weak",-25);weak.EffectData.unstackableGroup="group";weak.EffectData.unstackablePriority=1;f.TargetState.ApplyEffect(strong,f.Source);f.TargetState.ApplyEffect(weak,f.Source);Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,35,"weaker group effect stacked");});
            Test("StrongerUnstackableEffectReplacesWeakerProjection",()=>{var f=new Fixture();var weak=f.Effect("Weak",-25);weak.EffectData.unstackableGroup="group";weak.EffectData.unstackablePriority=1;var strong=f.Effect("Strong",-50);strong.EffectData.unstackableGroup="group";strong.EffectData.unstackablePriority=2;f.TargetState.ApplyEffect(weak,f.Source);f.TargetState.ApplyEffect(strong,f.Source);Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,30,"weaker modifier retained");});
            Test("StackedCancellationLimitRemovesOneInstance",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);e.EffectData.isStackable=true;f.TargetState.ApplyEffect(e,f.Source);f.TargetState.ApplyEffect(e,f.Source);f.Power.Power.PowerData.cancelEffectLimit=1;f.Power.EffectsCancelled.Add(e);f.EstimatePower(0,false);f.Power.EffectsCancelled.Clear();Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,35,"cancel limit removed wrong count");});
            Test("ZeroCancellationLimitRemovesAllInstances",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);e.EffectData.isStackable=true;f.TargetState.ApplyEffect(e,f.Source);f.TargetState.ApplyEffect(e,f.Source);f.Power.EffectsCancelled.Add(e);f.EstimatePower(0,false);f.Power.EffectsCancelled.Clear();Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,40,"cancel all retained stack");});
            Test("ExplicitAndTagCancellationsEachRespectLimit",()=>{var f=new Fixture();var e=f.Effect("Marked",-25,"marked");e.EffectData.isStackable=true;f.TargetState.ApplyEffect(e,f.Source);f.TargetState.ApplyEffect(e,f.Source);f.Power.Power.PowerData.cancelEffectLimit=1;f.Power.EffectsCancelled.Add(e);f.Power.CancelledEffectTags.Add("marked");f.EstimatePower(0,false);f.Power.EffectsCancelled.Clear();f.Power.CancelledEffectTags.Clear();Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,40,"independent cancellations deduplicated");});
            Test("CriticalPercentModifierUsesOriginalModifiedValueBasis",()=>{var f=new Fixture();var defense=new ModifiedValue(40);defense.AddModifiers(25,10);Set(f.Target.Unit,"criticalDefense",defense);f.ResetTarget();var e=f.Effect("CriticalPercent");e.UnitModifiers[0].ModifierData.modCriticalDefensePercent=50;f.TargetState.ApplyEffect(e,f.Source);Set(f.Power,"criticalDamageBonusPercent",50f);f.Hit(40,true);Equal(f.TargetState.health,56,"percent modifier used derived defense instead of original basis");Equal(f.Target.Unit.CriticalDefense,60,"live ModifiedValue changed");});
            Test("AttackCancellationRemovesProjectedEffectAtThreshold",()=>{var f=new Fixture();var e=f.Effect("Marked");e.EffectData.cancelledAfterNumAttacks=2;f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.EstimatePower(10,false);f.EstimatePower(10,false);f.Hit(60,true);Equal(f.TargetState.health,20,"hit-cancelled effect retained");});
            Test("StackedAttackCancellationDoesNotReadRemovedCounter",()=>{var f=new Fixture();var e=f.Effect("Marked");e.EffectData.isStackable=true;e.EffectData.cancelledAfterNumAttacks=1;f.TargetState.ApplyEffect(e,f.Source);f.TargetState.ApplyEffect(e,f.Source);f.EstimatePower(10,false);f.Vulnerability(e);f.Hit(60,true);Equal(f.TargetState.health,30,"stacked cancellation retained effect");});
            Test("CancelledGuardNoLongerRedirectsLaterAttack",()=>{var f=new Fixture();var e=f.Effect("Guard");e.EffectData.guarded=true;f.TargetState.ApplyEffect(e,f.Source);var states=new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>{{f.Source,f.SourceState},{f.Target,f.TargetState}};var guardians=new List<Combat.CombatUnit>();if(!f.TargetState.IsGuarded(states,ref guardians))throw new Exception("guard setup failed");f.Power.EffectsCancelled.Add(e);f.EstimatePower(0,false);guardians.Clear();if(f.TargetState.IsGuarded(states,ref guardians))throw new Exception("cancelled guard redirected later attack");});
            Test("CancelledReturnFireTriggersOnCancellingHitButNotNextHit",()=>{var f=new Fixture();f.ReactionPower();var e=f.Effect("ReturnFire");e.EffectData.returnFirePower="reaction";f.TargetState.ApplyEffect(e,f.Target);f.Power.EffectsCancelled.Add(e);var states=new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>{{f.Source,f.SourceState},{f.Target,f.TargetState}};f.EstimatePower(10,false);var first=f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Target,states);Equal(first.Count(r=>r.type==Combat.PowerReaction.ReactionType.ReturnFire),1,"return fire missing on cancelling hit");f.Power.EffectsCancelled.Clear();f.EstimatePower(10,false);var next=f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Target,states);Equal(next.Count(r=>r.type==Combat.PowerReaction.ReactionType.ReturnFire),0,"cancelled return fire reacted later");});
            Test("NewReturnFireCannotReactOnItsApplyingHit",()=>{var f=new Fixture();f.ReactionPower();var e=f.Effect("ReturnFire");e.EffectData.returnFirePower="reaction";e.EffectData.ignoreArmor=true;e.EffectData.ignoreShield=true;f.Power.EffectsApplied.Add(e);var states=new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>{{f.Source,f.SourceState},{f.Target,f.TargetState}};f.EstimatePower(10,false);var first=f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Target,states);Equal(first.Count(r=>r.type==Combat.PowerReaction.ReactionType.ReturnFire),0,"new return fire reacted on applying hit");f.Power.EffectsApplied.Clear();f.EstimatePower(10,false);var next=f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Target,states);Equal(next.Count(r=>r.type==Combat.PowerReaction.ReactionType.ReturnFire),1,"projected return fire missing later");});
            Test("CancelledOverwatchTriggersOnCancellingHitButNotNextHit",()=>{var f=new Fixture();f.ReactionPower();var e=f.Effect("Overwatch");e.EffectData.overwatchPower="reaction";f.SourceState.ApplyEffect(e,f.Target);f.Power.EffectsCancelled.Add(e);Set(f.Power,"damageMin",new ModifiedValue(10));Set(f.Power,"damageMax",new ModifiedValue(10));var states=new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>{{f.Source,f.SourceState},{f.Target,f.TargetState}};bool c;f.SourceState.EstimatePower(f.SourceState,f.CombatPower,new Combat.CombatTime(0,0),false,false,out c,false,false);var first=f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Source,states);Equal(first.Count(r=>r.type==Combat.PowerReaction.ReactionType.Overwatch),1,"overwatch missing on cancelling hit");f.Power.EffectsCancelled.Clear();f.SourceState.EstimatePower(f.SourceState,f.CombatPower,new Combat.CombatTime(0,0),false,false,out c,false,false);var next=f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Source,states);Equal(next.Count(r=>r.type==Combat.PowerReaction.ReactionType.Overwatch),0,"cancelled overwatch reacted later");});
            Test("ReactionSnapshotRestoredWhenNativeForecastThrows",()=>{var f=new Fixture();f.ReactionPower();var e=f.Effect("ReturnFire");e.EffectData.returnFirePower="reaction";f.TargetState.ApplyEffect(e,f.Target);f.Power.EffectsCancelled.Add(e);f.EstimatePower(10,false);var current=f.TargetState.returnFireEffects;var cached=(Dictionary<string,Power>)typeof(SRRWGame).GetField("powers",Flags).GetValue(f.Game);cached["reaction"].PowerData.tags=null;var states=new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>{{f.Source,f.SourceState},{f.Target,f.TargetState}};bool threw=false;try{f.Combat.GetPowerReactions(f.CombatPower,new Combat.CombatTime(0,0),f.Target,states);}catch(ArgumentNullException){threw=true;}if(!threw)throw new Exception("invalid reaction fixture did not throw");if(!Object.ReferenceEquals(current,f.TargetState.returnFireEffects))throw new Exception("reaction lists not restored after exception");Equal(current.Count,0,"cancelled return fire retained after exception");});
            Test("NoCancelPowerPreservesProjectedEffect",()=>{var f=new Fixture();var e=f.Effect("Marked");e.EffectData.cancelledAfterNumAttacks=1;f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);Set(f.Power,"noCancel",true);f.EstimatePower(10,false);f.Hit(60,true);Equal(f.TargetState.health,15,"NoCancel ignored");});
            Test("InterruptedForecastRemovesInterruptCancelledEffect",()=>{var f=new Fixture();var e=f.Effect("Marked");e.EffectData.cancelledOnInterrupt=true;f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.TargetState.hasPowerExecution=true;f.TargetState.powerExecutionTime=new Combat.CombatTime(0,20);f.TargetState.currentPower=f.CombatPower;f.TargetState.interruptThreshold=1;f.TargetState.currentStagger=1;Set(f.Power,"interruptDamageMin",new ModifiedValue(1));f.TargetState.EstimateDamage(f.SourceState,f.CombatPower,new Combat.CombatTime(0,0),10,false,false,true,false);f.TargetState.hasPowerExecution=false;f.Hit(60,true);Equal(f.TargetState.health,30,"interrupt-cancelled effect retained");});
            Test("CriticalReflectionReactionAndScenarioMatchExecution",()=>{var f=new Fixture();f.Reflect(45,50,0,true,1,7,93);});
            Test("NormalReflectionStillUsesBaseDamage",()=>{var f=new Fixture();f.Reflect(45,50,0,false,1,4,96);});
            Test("CriticalReflectionRespectsDefenseBeforeProjectedEffects",()=>{var f=new Fixture();f.Reflect(40,50,50,true,1,5,95);});
            Test("ProjectedCriticalDefenseFeedsReflection",()=>{var f=new Fixture();var e=f.Effect("Marked",-25);f.TargetState.ApplyEffect(e,f.Source);f.Reflect(40,50,0,true,1,6,94);});
            Test("CriticalReflectionMultipleHitsPreserveRoundingAndArmor",()=>{var f=new Fixture();f.Reflect(45,50,0,true,3,7,79);});
            Test("CriticalReflectionPreservesRecipientArmor",()=>{var f=new Fixture();Set(f.Source.Unit,"currentArmor",15);Set(f.Source.Unit,"armorMax",new ModifiedValue(15));f.SourceState.armor=15;f.SourceState.armorMax=new ModifiedValue(15);f.Reflect(45,50,0,true,1,0,100);Equal(f.SourceState.armor,15,"reflection stripped recipient armor");Equal(f.Source.Unit.CurrentArmor,15,"live recipient armor changed");});
            Test("CriticalReflectionMultipleHitsConsumeRecipientShields",()=>{var f=new Fixture();Set(f.Source.Unit,"currentShield",20);Set(f.Source.Unit,"shieldCap",new ModifiedValue(20));f.SourceState.shield=20;f.SourceState.shieldCap=new ModifiedValue(20);f.Reflect(45,50,0,true,3,0,99);Equal(f.SourceState.shield,0,"reflected multihit shield consumption");Equal(f.Source.Unit.CurrentShield,20,"live recipient shield changed");});
            Test("ReflectionIgnoresIncomingVulnerabilityAndDamageReduction",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);Set(f.Target.Unit,"reflectDamageReduction",new ModifiedValue(100));f.Reflect(45,50,0,true,1,7,93);});
            Test("ReflectionFirstStrikeUsesBaseBonusBeforeTargetReduction",()=>{var f=new Fixture();f.TargetState.susceptibleToFirstStrike=true;f.Game.Rules.firstStrikeDamageBonus=25;Set(f.Power,"noFirstStrike",false);f.Reflect(40,50,0,true,1,7,93);});
            Test("ReflectionCapturesCritBeforeHitStaggersTarget",()=>{var f=new Fixture();Set(f.Power,"timeDamage",new ModifiedValue(15));f.TargetState.currentStagger=1;f.TargetState.currentPower=f.CombatPower;f.Reflect(45,50,0,true,1,7,93,true);});
            Test("ProjectedMarkedForecastMatchesUnpatchedExecutionMethod",()=>{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.Hit(60,true);f.AddLive(e);float reflected;Equal(f.NativeDamage(f.Target,60,true,0,0,out reflected),75,"native marked damage changed");Equal(f.TargetState.health,25,"forecast/native execution mismatch");});
            Test("CriticalReflectionForecastMatchesUnpatchedExecutionMethods",()=>{var f=new Fixture();f.Reflect(45,50,0,true,1,7,93);float reflected;f.NativeDamage(f.Target,67.5f,true,22.5f,10,out reflected);Equal(reflected,6.75f,"native reflected raw changed");float ignored;float damage=f.NativeDamage(f.Source,reflected,false,0,0,out ignored);Equal(f.NativeApplyDamage(f.Source,damage),93,"native rounded reflected health changed");});
#if FOLLOWUP_FIX
            Test("DryRunPreflightDoesNotInstallHooks",()=>{var h=new Harmony("test.forecast.preflight");int before=Harmony.GetAllPatchedMethods().Count();ProjectedEffectFix.ValidateTargets();ReflectionPreviewFix.ValidateReactionTargets();Equal(Harmony.GetAllPatchedMethods().Count(),before,"preflight installed hooks");});
            Test("ProjectedInstructionEditsPreserveLabelsAndBlocks",()=>{
                var original=PatchProcessor.GetOriginalInstructions(ReflectionPreviewFix.ResolveTarget()).ToList();var generator=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator();for(int i=0;i<10000;i++)generator.DefineLabel();
                var methods=new[]{typeof(Unit).GetMethod("HasEffect",new[]{typeof(Effect)}),typeof(Unit).GetProperty("Effects").GetGetMethod(),typeof(Unit).GetProperty("CriticalDefense").GetGetMethod()};
                foreach(var method in methods){var site=original.Single(c=>c.Calls(method));site.labels.Add(generator.DefineLabel());site.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));}
                var stores=original.Where(c=>c.opcode==OpCodes.Starg_S&&Convert.ToInt32(c.operand)==4).ToList();foreach(var store in stores){store.labels.Add(generator.DefineLabel());store.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));}
                var patched=ProjectedEffectFix.Transpile(original).ToList();foreach(var instruction in original.Where(c=>c.labels.Count>0)){var moved=patched.Single(c=>c.labels.Contains(instruction.labels[0]));Equal(moved.blocks.Count,instruction.blocks.Count,"exception block lost");if(instruction.opcode==OpCodes.Starg_S&&moved.opcode!=OpCodes.Dup&&moved.opcode!=OpCodes.Starg_S)throw new Exception("invalid starg branch entry");}
                foreach(var method in methods)if(!original.Single(c=>c.Calls(method)).labels.Any())throw new Exception("input labels mutated");
            });
            Test("ForecastTranspilersComposeInEitherOrder",()=>{var original=PatchProcessor.GetOriginalInstructions(ReflectionPreviewFix.ResolveTarget()).ToArray();foreach(var patched in new[]{ProjectedEffectFix.Transpile(ReflectionPreviewFix.Transpile(original)).ToArray(),ReflectionPreviewFix.Transpile(ProjectedEffectFix.Transpile(original)).ToArray()}){if(patched.Count(c=>c.Calls(typeof(ReflectionPreviewFix).GetMethod("ArmorDamageForEstimate")))!=1||patched.Count(c=>c.Calls(typeof(ProjectedEffectFix).GetMethod("CriticalDefenseForEstimate")))!=1)throw new Exception("combined hooks missing");}bool rejected=false;try{ProjectedEffectFix.Transpile(new CodeInstruction[0]).ToArray();}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("unsupported projection IL accepted");});
            Test("DisabledProjectionKeepsNativeBehavior",()=>{ProjectedEffectFix.Enabled=false;try{var f=new Fixture();var e=f.Effect("Marked");f.Vulnerability(e);f.TargetState.ApplyEffect(e,f.Source);f.Hit(60,true);Equal(f.TargetState.health,40,"disabled helper changed native damage");}finally{ProjectedEffectFix.Enabled=true;}});
#endif
        }
        finally{harmony.UnpatchAll(harmony.Id);ReflectionPreviewFix.Enabled=false;
#if FOLLOWUP_FIX
            ProjectedEffectFix.Enabled=false;
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
            var u=Blank<Unit>();Init(u,Game,id);Set(u,"currentHealth",100);Set(u,"maximumHealth",new ModifiedValue(100));Set(u,"damage",new ModifiedValue(100));
            var cu=Blank<Combat.CombatUnit>();Init(cu,Game,id);Set(cu,"unit",u);Set(cu,"combat",Combat);return cu;
        }
        public void ResetTarget(){TargetState=new Combat.CombatUnit.EstimatedState(Target);}
        public Effect Effect(string id,float criticalDefense=0,string tag=null)
        {
            var e=Blank<Effect>();Init(e,Game,id);var data=new EffectData{id=id,tags=tag==null?new string[0]:new[]{tag}};((UnityEngine.ISerializationCallbackReceiver)data).OnAfterDeserialize();Set(e,"effectData",data);
            var mod=Blank<UnitModifier>();Init(mod,Game,id+"mod");Set(mod,"modifierData",new UnitModifierData{tags=new string[0],modCriticalDefenseAbsolute=criticalDefense});Set(e,"unitModifiers",new List<UnitModifier>{mod});return e;
        }
        public void AddLive(Effect e){Target.Unit.Effects.Add(new UnitEffect(Target.Unit,e));}
        public void ReactionPower()
        {
            var data=new PowerData{id="reaction",damageMin=10,damageMax=10,noFirstStrike=true,hostility="Enemy",target="Single"};foreach(var field in typeof(PowerData).GetFields())if(field.FieldType.IsArray&&field.GetValue(data)==null)field.SetValue(data,Array.CreateInstance(field.FieldType.GetElementType(),0));
            var power=Blank<Power>();Init(power,Game,"reaction");Set(power,"powerData",data);((Dictionary<string,Power>)typeof(SRRWGame).GetField("powers",Flags).GetValue(Game)).Add("reaction",power);
        }
        Combat.ExecutePowerEvent nativeEvent;Type actualType;IDictionary actualStates;
        void NativeState()
        {
            if(nativeEvent!=null)return;
            nativeEvent=Blank<Combat.ExecutePowerEvent>();Init(nativeEvent,Game,"event");actualType=typeof(Combat.ExecutePowerEvent).GetNestedType("ActualUnitState",BindingFlags.NonPublic);
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
        public void Vulnerability(Effect effect,string tag=null)
        {
            var v=Blank<Vulnerability>();Init(v,Game,"vulnerability");var d=new VulnerabilityData{id="vulnerability",modDamagePercent=25,validDifficulties=new string[0],removeCancelEffectsByTag=new string[0],effectsByTag=tag==null?new string[0]:new[]{tag}};d.OnAfterDeserialize();Set(v,"vulnerabilityData",d);Set(v,"effect",effect);Power.CriticalVulnerabilities.Add(v);
        }
        public void Hit(float damage,bool critical){TargetState.EstimateDamage(SourceState,CombatPower,new Combat.CombatTime(0,0),damage,critical,false,false,false);}
        public void EstimatePower(float damage,bool critical){Set(Power,"damageMin",new ModifiedValue(damage));Set(Power,"damageMax",new ModifiedValue(damage));TargetState.hasPowerExecution=critical;TargetState.powerExecutionTime=new Combat.CombatTime(0,10);bool c;TargetState.EstimatePower(SourceState,CombatPower,new Combat.CombatTime(0,0),false,true,out c,false,false);}
        public void Reflect(float damage,float bonus,float defense,bool critical,int hits,float perReactionDamage,float totalHealth,bool allowStagger=false)
        {
            Set(Power,"damageMin",new ModifiedValue(damage));Set(Power,"damageMax",new ModifiedValue(damage));Set(Power,"criticalDamageBonusPercent",bonus);Set(Power,"numAttacks",hits);Set(Target.Unit,"criticalDefense",new ModifiedValue(defense));Set(Target.Unit,"reflectDamage",new ModifiedValue(10));if(defense!=0)ResetTarget();if(hits>1)TargetState.health=1000;
            // The original incoming power has armor damage, which reflection must never reuse.
            Set(Power,"armorDamageMin",new ModifiedValue(10));
            TargetState.hasPowerExecution=critical;TargetState.powerExecutionTime=new Combat.CombatTime(0,10);
            var states=new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>{{Source,SourceState},{Target,TargetState}};
            var scenario=new Dictionary<Combat.CombatUnit,Combat.UnitResults>();
            var estimate=typeof(Combat).GetMethod("EstimatePowerAgainstTargets",Flags);
            // The game invokes this once per hit. It gets reactions after the hit and then accumulates them.
            for(int i=0;i<hits;i++)estimate.Invoke(Combat,new object[]{states,CombatPower,scenario,new List<Combat.CombatUnit>{Target},allowStagger&&i==0,false,null,null});
            Equal(SourceState.health,totalHealth,"scenario reflected health");
            foreach(var reaction in scenario[Source].powerReactions)Equal(reaction.estimatedResults.healthDelta,perReactionDamage,"reaction card reflected damage");
            Equal(Source.Unit.CurrentHealth,100,"live source health changed");Equal(Target.Unit.CurrentHealth,100,"live target health changed");Equal(Power.ArmorDamageMin,10,"source armor damage changed");
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
