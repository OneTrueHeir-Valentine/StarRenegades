using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mdi.SRRW.Engine;

namespace SRBugfix
{
    public static class ProjectedEffectFix
    {
        internal static bool Enabled=false;
        static readonly ConditionalWeakTable<Combat.CombatUnit.EstimatedState,Projection> states=new ConditionalWeakTable<Combat.CombatUnit.EstimatedState,Projection>();
        static readonly FieldInfo criticalDefense=AccessTools.Field(typeof(Unit),"criticalDefense");
        sealed class Projection
        {
            internal readonly GameElementCollection<UnitEffect> Effects=new GameElementCollection<UnitEffect>();
            internal readonly ModifiedValue CriticalDefense;
            internal readonly Stack<PowerApplication> Applications=new Stack<PowerApplication>();
            internal readonly Dictionary<UnitEffect,int> CancellableHits=new Dictionary<UnitEffect,int>();
            internal Projection(Combat.CombatUnit.EstimatedState state)
            {
                if(criticalDefense==null||criticalDefense.FieldType!=typeof(ModifiedValue))throw new InvalidDataException("Supported critical defense field was not found.");
                CriticalDefense=((ModifiedValue)criticalDefense.GetValue(state.Unit.Unit)).DeepCopy();
                foreach(var effect in state.Unit.Unit.Effects)
                {
                    Effects.Add(effect);
                    CancellableHits[effect]=state.Unit.Effects.Has(effect.ID)?state.Unit.Effects[effect.ID].CancellableHits:0;
                }
            }
        }
        sealed class PowerApplication
        {
            internal Combat.CombatUnitPower Power;
            internal int VulnerabilityStart;
            internal float ThroughShield,ThroughArmor;
        }
        static Projection Get(Combat.CombatUnit.EstimatedState state){return states.GetValue(state,s=>new Projection(s));}
        public static void ValidateTargets()
        {
            var type=typeof(Combat.CombatUnit.EstimatedState);
            if(type.GetConstructor(new[]{typeof(Combat.CombatUnit)})==null||type.GetMethod("ApplyEffect",new[]{typeof(Effect),typeof(Combat.CombatUnit)})==null||type.GetMethod("EstimatePower")==null||criticalDefense==null||criticalDefense.FieldType!=typeof(ModifiedValue))throw new InvalidDataException("Supported projected effect targets were not found.");
            Transpile(PatchProcessor.GetOriginalInstructions(ReflectionPreviewFix.ResolveTarget())).ToArray();
        }
        public static void Install(Harmony harmony)
        {
            var type=typeof(Combat.CombatUnit.EstimatedState);
            var ctor=type.GetConstructor(new[]{typeof(Combat.CombatUnit)});
            var apply=type.GetMethod("ApplyEffect",new[]{typeof(Effect),typeof(Combat.CombatUnit)});
            var damage=ReflectionPreviewFix.ResolveTarget();
            if(ctor==null||apply==null)throw new InvalidDataException("Supported projected effect targets were not found.");
            // Validate the native layout before installing any hooks.
            ValidateTargets();
            harmony.Patch(ctor,postfix:Hook("Initialize"));
            harmony.Patch(apply,prefix:Hook("ApplyEffect"));
            harmony.Patch(damage,prefix:Hook("BeforeDamage"),postfix:Hook("AfterDamage"),transpiler:Hook("Transpile"));
            harmony.Patch(type.GetMethod("EstimatePower"),prefix:Hook("BeforePower"),finalizer:Hook("AfterPower"));
        }
        static HarmonyMethod Hook(string name){return new HarmonyMethod(typeof(ProjectedEffectFix).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic));}
        static void Initialize(Combat.CombatUnit.EstimatedState __instance){if(Enabled)Get(__instance);}
        static bool ApplyEffect(Combat.CombatUnit.EstimatedState __instance,Effect effect)
        {
            if(!Enabled)return true;
            var p=Get(__instance);var unit=__instance.Unit.Unit;
            if(!unit.CanApplyEffect(effect))return false;
            if(p.Applications.Count>0)
            {
                var application=p.Applications.Peek();
                if(application.Power.Power.Hostility!=Power.PowerHostility.Ally&&((!effect.EffectData.ignoreShield&&application.ThroughShield<=0)||(!effect.EffectData.ignoreArmor&&application.ThroughArmor<=0)))return false;
                for(int i=application.VulnerabilityStart;i<__instance.exploitedVulnerabilities.Count;i++)if(effect.EffectData.tagsHash.Overlaps(__instance.exploitedVulnerabilities[i].VulnerabilityData.blockEffectsByTag))return false;
            }
            if(!effect.EffectData.isStackable&&!String.IsNullOrEmpty(effect.EffectData.unstackableGroup))
            {
                for(int i=0;i<p.Effects.Count&&p.Effects[i].Effect!=effect;i++)
                {
                    var previous=p.Effects[i];
                    if(previous.Effect.EffectData.unstackableGroup!=effect.EffectData.unstackableGroup)continue;
                    if(previous.Effect.EffectData.unstackablePriority>effect.EffectData.unstackablePriority)return false;
                    Remove(__instance,previous);i--;
                }
            }
            if(!effect.EffectData.isStackable&&p.Effects.Any(e=>e.Effect==effect))return false;
            var projected=new UnitEffect(unit,effect);p.Effects.Add(projected);p.CancellableHits[projected]=0;ModifyCriticalDefense(__instance,effect,1);
            return true;
        }
        static void ModifyCriticalDefense(Combat.CombatUnit.EstimatedState state,Effect effect,int sign)
        {
            foreach(var modifier in effect.UnitModifiers)
            {
                var d=modifier.ModifierData;
                if(d.tags.Length==0||state.Unit.Unit.Tags.IsSupersetOf(d.tags))Get(state).CriticalDefense.AddModifiers(sign*d.modCriticalDefensePercent,sign*d.modCriticalDefenseAbsolute);
            }
        }
        public static float CriticalDefenseForEstimate(Unit unit,Combat.CombatUnit.EstimatedState state){return Enabled?Get(state).CriticalDefense.Value:unit.CriticalDefense;}
        public static bool HasEffectForEstimate(Unit unit,Effect effect,Combat.CombatUnit.EstimatedState state){return Enabled?Get(state).Effects.Any(e=>e.Effect==effect):unit.HasEffect(effect);}
        public static GameElementCollection<UnitEffect> EffectsForEstimate(Unit unit,Combat.CombatUnit.EstimatedState state){return Enabled?Get(state).Effects:unit.Effects;}
        static void BeforePower(Combat.CombatUnit.EstimatedState __instance,Combat.CombatUnitPower power,out PowerApplication __state)
        {
            __state=null;if(!Enabled)return;
            __state=new PowerApplication{Power=power,VulnerabilityStart=__instance.exploitedVulnerabilities.Count};Get(__instance).Applications.Push(__state);
        }
        static Exception AfterPower(Combat.CombatUnit.EstimatedState __instance,PowerApplication __state,Exception __exception)
        {
            if(__state!=null)Get(__instance).Applications.Pop();return __exception;
        }
        public static void RecordThroughShield(float damage,Combat.CombatUnit.EstimatedState state){if(Enabled&&Get(state).Applications.Count>0)Get(state).Applications.Peek().ThroughShield=damage;}
        public static void RecordThroughArmor(float damage,Combat.CombatUnit.EstimatedState state){if(Enabled&&Get(state).Applications.Count>0)Get(state).Applications.Peek().ThroughArmor=damage;}
        sealed class DamageStart { internal int VulnerabilityStart;internal bool WasInterrupted; }
        static void BeforeDamage(Combat.CombatUnit.EstimatedState __instance,out DamageStart __state){__state=new DamageStart{VulnerabilityStart=__instance.exploitedVulnerabilities.Count,WasInterrupted=__instance.wasInterrupted};if(Enabled)Get(__instance);}
        static void AfterDamage(Combat.CombatUnit.EstimatedState __instance,Combat.CombatUnitPower power,bool criticalHit,bool isReflectDamage,DamageStart __state)
        {
            if(!Enabled||isReflectDamage)return;
            var tags=new HashSet<string>(power.Power.CancelledEffectTags);
            if(criticalHit)tags.UnionWith(power.Power.CriticalCancelEffectsByTag);
            for(int i=__state.VulnerabilityStart;i<__instance.exploitedVulnerabilities.Count;i++)tags.ExceptWith(__instance.exploitedVulnerabilities[i].VulnerabilityData.removeCancelEffectsByTag);
            var p=Get(__instance);
            int limit=power.Power.Power.PowerData.cancelEffectLimit;
            if(power.Power.DamageMax>0&&!power.Power.NoCancel)
            {
                var cancelledOnHit=new List<Effect>();
                foreach(var effect in p.Effects.Where(e=>e.Effect.EffectData.cancelledAfterNumAttacks>0).ToArray())
                {
                    p.CancellableHits[effect]++;
                    if(p.CancellableHits[effect]>=effect.Effect.EffectData.cancelledAfterNumAttacks)cancelledOnHit.Add(effect.Effect);
                }
                foreach(var effect in cancelledOnHit)Cancel(__instance,effect,limit);
            }
            foreach(var effect in power.Power.EffectsCancelled)Cancel(__instance,effect,limit);
            if(!__state.WasInterrupted&&__instance.wasInterrupted)foreach(var effect in p.Effects.Where(e=>e.Effect.EffectData.cancelledOnInterrupt).Select(e=>e.Effect).ToArray())Cancel(__instance,effect,limit);
            foreach(var effect in p.Effects.Where(e=>e.Effect.EffectData.tagsHash.Overlaps(tags)).Select(e=>e.Effect).ToArray())Cancel(__instance,effect,limit);
        }
        static void Cancel(Combat.CombatUnit.EstimatedState state,Effect effect,int limit)
        {
            int remaining=limit;
            foreach(var e in Get(state).Effects.Where(e=>e.Effect==effect).ToArray())
            {
                Remove(state,e);
                if(!effect.EffectData.isStackable||--remaining==0)break;
            }
        }
        static void Remove(Combat.CombatUnit.EstimatedState state,UnitEffect effect)
        {
            Get(state).Effects.Remove(effect.ID);Get(state).CancellableHits.Remove(effect);ModifyCriticalDefense(state,effect.Effect,-1);
            // Each reactive list has one entry per applicable stack, including projected stacks.
            foreach(var list in new[]{state.overwatchEffects,state.returnFireEffects,state.guardingEffects})
            {
                var reactive=list.FirstOrDefault(e=>e.Effect.Effect==effect.Effect);if(reactive!=null)list.Remove(reactive);
            }
            foreach(var modifier in effect.Effect.UnitModifiers)
            {
                var d=modifier.ModifierData;if(d.tags.Length!=0&&!state.Unit.Unit.Tags.IsSupersetOf(d.tags))continue;
                state.healthDefense.AddModifiers(-d.modHealthDefensePercent,-d.modHealthDefenseAbsolute);state.armorDefense.AddModifiers(-d.modArmorDefensePercent,-d.modArmorDefenseAbsolute);state.shieldDefense.AddModifiers(-d.modShieldDefensePercent,-d.modShieldDefenseAbsolute);
            }
        }
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
            var calls=new[]{typeof(Unit).GetMethod("HasEffect",new[]{typeof(Effect)}),typeof(Unit).GetProperty("Effects").GetGetMethod(),typeof(Unit).GetProperty("CriticalDefense").GetGetMethod()};
            var helpers=new[]{"HasEffectForEstimate","EffectsForEstimate","CriticalDefenseForEstimate"};
            for(int n=0;n<calls.Length;n++)
            {
                var sites=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(calls[n])).ToArray();
                if(sites.Length!=1)throw new InvalidDataException("Unsupported projected effect forecast layout.");
                int at=sites[0];var arg=new CodeInstruction(OpCodes.Ldarg_0);arg.labels.AddRange(code[at].labels);code[at].labels.Clear();arg.blocks.AddRange(code[at].blocks);code[at].blocks.Clear();
                code[at].opcode=OpCodes.Call;code[at].operand=typeof(ProjectedEffectFix).GetMethod(helpers[n]);code.Insert(at,arg);
            }
            foreach(var resource in new[]{"shield","armor"})
            {
                var setter=typeof(Combat.CombatUnit.EstimatedState).GetProperty(resource).GetSetMethod();
                var sites=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(setter)).ToArray();
                if(sites.Length!=1)throw new InvalidDataException("Unsupported projected damage resource layout.");
                int store=-1;
                for(int i=sites[0]+1;i<Math.Min(code.Count,sites[0]+16);i++)if(code[i].opcode==OpCodes.Starg_S&&Convert.ToInt32(code[i].operand)==4){store=i;break;}
                if(store<0)throw new InvalidDataException("Supported damage-through-resource estimate was not found.");
                var duplicate=new CodeInstruction(OpCodes.Dup);duplicate.labels.AddRange(code[store].labels);code[store].labels.Clear();duplicate.blocks.AddRange(code[store].blocks);code[store].blocks.Clear();
                code.InsertRange(store,new[]{duplicate,new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,typeof(ProjectedEffectFix).GetMethod(resource=="shield"?"RecordThroughShield":"RecordThroughArmor"))});
            }
            return code;
        }
    }
}
