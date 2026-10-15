using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;

namespace SRBugfix
{
    public static class EffectLifecycleFix
    {
        public static bool Enabled=false;
        public static void Install(Harmony harmony){ProjectedEffectFix.InstallLifecycle(harmony);}
        public static void ValidateTargets(){ProjectedEffectFix.ValidateLifecycleTargets();}
        public static void ValidateImmunityDomain(GameData data)
        {
            if(data==null||data.EffectData==null||data.UnitModifierData==null)throw new InvalidDataException("Effect immunity data is missing.");
            // Copying final native tag counts is additive only; a later modifier removal can hide a new projected grant.
            foreach(var modifier in data.UnitModifierData.Values)if((modifier.removeEffectImmunitiesByTag??new string[0]).Length!=0)throw new InvalidDataException("Unsupported ordered immunity tag removal: "+modifier.id);
            foreach(var effect in data.EffectData.Values)foreach(var id in effect.unitModifiers??new string[0])
            {
                UnitModifierData modifier;if(data.UnitModifierData.TryGetValue(id,out modifier))ProjectedEffectFix.ValidateImmunityModifier(modifier);
            }
        }
    }
    public static partial class ProjectedEffectFix
    {
        internal static event Action<Combat.CombatUnit.EstimatedState,UnitEffect> ProjectedEffectAdded=delegate{};
        internal static event Action<Combat.CombatUnit.EstimatedState,UnitEffect> ProjectedEffectRemoved=delegate{};
        sealed class EffectReference
        {
            internal Combat.CombatUnit.EstimatedState State;
            internal UnitEffect Effect;
        }
        sealed partial class Projection
        {
            internal readonly List<string> BaseImmunityTags=new List<string>();
            internal readonly Dictionary<UnitEffect,List<EffectReference>> Links=new Dictionary<UnitEffect,List<EffectReference>>();
            internal readonly HashSet<UnitEffect> CapacityContributors=new HashSet<UnitEffect>();
        }
        sealed class PendingGrant
        {
            internal Combat.CombatUnit.EstimatedState State;
            internal Effect Effect;
            internal Combat.CombatUnit Source;
        }
        sealed class ActionQueue
        {
            internal readonly List<PendingGrant> Targets=new List<PendingGrant>();
            internal readonly List<EffectReference> Granted=new List<EffectReference>();
            internal bool Failed,Committing,HadTargets;
        }
        sealed class PendingActions
        {
            internal readonly Dictionary<Combat.CombatUnitPower,ActionQueue> Queues=new Dictionary<Combat.CombatUnitPower,ActionQueue>();
        }
        static readonly ConditionalWeakTable<Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>,PendingActions> pendingActions=new ConditionalWeakTable<Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>,PendingActions>();
        sealed class LifecycleContext
        {
            internal Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> States;
            internal Combat.CombatUnitPower Power;
            internal ActionQueue Queue;
            internal bool Pending;
        }
        [ThreadStatic] static Stack<LifecycleContext> lifecycleContexts;
        static bool LifecycleActive{get{return Enabled&&EffectLifecycleFix.Enabled;}}
        static LifecycleContext CurrentLifecycle{get{return lifecycleContexts!=null&&lifecycleContexts.Count>0?lifecycleContexts.Peek():null;}}
        internal static void ValidateImmunityModifier(UnitModifierData d)
        {
            if((d.addEffectImmunities??new string[0]).Length!=0||(d.removeEffectImmunities??new string[0]).Length!=0||(d.removeEffectImmunitiesByTag??new string[0]).Length!=0)
                throw new InvalidDataException("Unsupported effect-owned immunity modifier: "+d.id);
        }
        static IEnumerable<UnitModifierData> EligibleImmunityModifiers(Combat.CombatUnit.EstimatedState state,Effect effect)
        {
            foreach(var modifier in effect.UnitModifiers)
            {
                var d=modifier.ModifierData;ValidateImmunityModifier(d);
                if(d.tags==null||d.tags.Length==0||state.Unit.Unit.Tags.IsSupersetOf(d.tags))yield return d;
            }
        }
        static void InitializeLifecycle(Combat.CombatUnit.EstimatedState state,Projection projection)
        {
            // The native list preserves duplicate innate, trait, equipment and effect grants.
            projection.BaseImmunityTags.AddRange(state.Unit.Unit.EffectImmunitiesByTag);
            projection.CapacityContributors.UnionWith(projection.Effects);
            foreach(var e in projection.Effects)foreach(var modifier in e.Effect.UnitModifiers)
            {
                var d=modifier.ModifierData;if(LifecycleActive)ValidateImmunityModifier(d);
                if(d.tags==null||d.tags.Length==0||state.Unit.Unit.Tags.IsSupersetOf(d.tags))foreach(var tag in d.addEffectImmunitiesByTag??new string[0])projection.BaseImmunityTags.Remove(tag);
            }
        }
        public static bool CanApplyEffectForEstimate(Unit unit,Effect effect,Combat.CombatUnit.EstimatedState state)
        {
            if(!LifecycleActive)return unit.CanApplyEffect(effect);
            if(unit.EffectImmunities.Has(effect))return false;
            var p=Get(state);var tags=new HashSet<string>(p.BaseImmunityTags);
            foreach(var e in p.Effects)foreach(var d in EligibleImmunityModifiers(state,e.Effect))tags.UnionWith(d.addEffectImmunitiesByTag??new string[0]);
            if(effect.EffectData.tagsHash.Overlaps(tags))return false;
            return effect.EffectData.requiredUnitTags==null||unit.Tags.IsSupersetOf(effect.EffectData.requiredUnitTags);
        }
        static void RefreshForLifecycle(Combat.CombatUnit.EstimatedState state,Effect effect,Combat.CombatUnit source)
        {
            if(!LifecycleActive)return;
            foreach(var list in new[]{state.overwatchEffects,state.returnFireEffects,state.guardingEffects})
                for(int i=0;i<list.Count;i++)if(list[i].Effect.Effect==effect)list[i]=new Combat.CombatUnitEffect(state.Unit.Combat,state.Unit,list[i].Effect,source,false);
        }
        static void AddedForLifecycle(Combat.CombatUnit.EstimatedState state,UnitEffect effect)
        {
            if(LifecycleActive)
            {
                bool nativePrecomputed=Get(state).Applications.Count>0&&!QueuesTargetEffects(state);
                if(!nativePrecomputed)ModifyLifecycleCapacity(state,effect.Effect,1);
                Get(state).CapacityContributors.Add(effect);
            }
            ProjectedEffectAdded(state,effect);
            if(LifecycleActive&&CurrentLifecycle!=null)CurrentLifecycle.Queue.Granted.Add(new EffectReference{State=state,Effect=effect});
        }
        static Combat.CombatUnit.EstimatedState FindLifecycleState(LifecycleContext context,Combat.CombatUnit unit)
        {
            Combat.CombatUnit.EstimatedState state;if(!context.States.TryGetValue(unit,out state)){state=new Combat.CombatUnit.EstimatedState(unit);context.States.Add(unit,state);}return state;
        }
        static void RemovedForLifecycle(Combat.CombatUnit.EstimatedState state,UnitEffect effect)
        {
            ProjectedEffectRemoved(state,effect);
            if(!LifecycleActive)return;
            if(Get(state).CapacityContributors.Remove(effect))ModifyLifecycleCapacity(state,effect.Effect,-1);
            var context=CurrentLifecycle;if(context==null)return;
            var links=new List<EffectReference>();List<EffectReference> projected;
            if(Get(state).Links.TryGetValue(effect,out projected)){links.AddRange(projected);Get(state).Links.Remove(effect);}
            if(state.Unit.Effects.Has(effect.ID))foreach(var tether in state.Unit.Effects[effect.ID].TetheredEffects)
            {
                var other=FindLifecycleState(context,tether.Unit);var match=Get(other).Effects.FirstOrDefault(e=>e.ID==tether.Effect.ID);
                if(match!=null)links.Add(new EffectReference{State=other,Effect=match});
            }
            // Remove first, then traverse: native tether graphs include cycles.
            foreach(var link in links)if(Get(link.State).Effects.Any(e=>e==link.Effect))Remove(link.State,link.Effect);
        }
        static bool QueuesTargetEffects(Combat.CombatUnit.EstimatedState state)
        {
            var context=CurrentLifecycle;
            return LifecycleActive&&context!=null&&!context.Queue.Committing&&context.Power.Power.TetherAllEffects&&Get(state).Applications.Count>0&&Get(state).Applications.Peek().Power==context.Power;
        }
        static bool EffectReachesTarget(Combat.CombatUnit.EstimatedState state,Effect effect)
        {
            var app=Get(state).Applications.Peek();
            return app.Power.Power.Hostility==Power.PowerHostility.Ally||((effect.EffectData.ignoreShield||app.ThroughShield>0)&&(effect.EffectData.ignoreArmor||app.ThroughArmor>0));
        }
        static bool QueueTargetEffect(Combat.CombatUnit.EstimatedState state,Effect effect,Combat.CombatUnit source)
        {
            if(!QueuesTargetEffects(state))return false;
            CurrentLifecycle.Queue.Targets.Add(new PendingGrant{State=state,Effect=effect,Source=source});return true;
        }
        static void RejectTargetEffect(Combat.CombatUnit.EstimatedState state)
        {
            if(QueuesTargetEffects(state))CurrentLifecycle.Queue.Failed=true;
        }
        public static bool PrepareEffectForEstimate(Unit unit,Effect effect,Combat.CombatUnit.EstimatedState state)
        {
            // Tethered grants are materialized together after all attacks and cleanses.
            return !QueuesTargetEffects(state)&&CanApplyEffectForEstimate(unit,effect,state);
        }
        public static bool CanApplyTargetEffectForEstimate(Unit unit,Effect effect,Combat.CombatUnit.EstimatedState state)
        {
            bool accepted=CanApplyEffectForEstimate(unit,effect,state);
            if(!accepted&&QueuesTargetEffects(state)&&EffectReachesTarget(state,effect))RejectTargetEffect(state);
            return accepted;
        }
        static void ModifyLifecycleCapacity(Combat.CombatUnit.EstimatedState state,Effect effect,int sign)
        {
            state.armorMax.AddAbsoluteModifier(-sign*Math.Max(0,effect.EffectData.armorWeakenAbsolute));state.armorWeaken.AddAbsoluteModifier(sign*Math.Max(0,effect.EffectData.armorWeakenAbsolute));
            foreach(var modifier in effect.UnitModifiers)
            {
                var d=modifier.ModifierData;if(d.tags!=null&&d.tags.Length>0&&!state.Unit.Unit.Tags.IsSupersetOf(d.tags))continue;
                state.armorMax.AddModifiers(sign*d.modArmorMaxPercent,sign*d.modArmorMaxAbsolute);state.shieldCap.AddModifiers(sign*d.modShieldCapPercent,sign*d.modShieldCapAbsolute);
            }
        }
        static void PushLifecycle(LifecycleContext context)
        {
            if(lifecycleContexts==null)lifecycleContexts=new Stack<LifecycleContext>();lifecycleContexts.Push(context);
        }
        static void BeforeLifecycle(Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> __0,Combat.CombatUnitPower __1,bool __4,List<Combat.CombatUnit> __3,out LifecycleContext __state)
        {
            __state=null;if(!LifecycleActive)return;
            var pending=pendingActions.GetValue(__0,s=>new PendingActions());ActionQueue queue;
            if(__4||!pending.Queues.TryGetValue(__1,out queue)){queue=new ActionQueue();pending.Queues[__1]=queue;}
            queue.HadTargets|=__3!=null&&__3.Count>0;
            __state=new LifecycleContext{States=__0,Power=__1,Queue=queue,Pending=true};PushLifecycle(__state);
        }
        static bool DeferSourceEffects(){return !LifecycleActive;}
        static void CancelLifecycleSets(Combat.CombatUnit.EstimatedState state,IEnumerable<Effect> effects,HashSet<string> tags,int limit)
        {
            foreach(var effect in effects)Cancel(state,effect,limit);
            foreach(var effect in Get(state).Effects.Where(e=>e.Effect.EffectData.tagsHash.Overlaps(tags)).Select(e=>e.Effect).ToArray())Cancel(state,effect,limit);
        }
        static void CompleteLifecycle(LifecycleContext context)
        {
            var power=context.Power.Power;var source=FindLifecycleState(context,context.Power.Unit);int limit=power.Power.PowerData.cancelEffectLimit;
            CancelLifecycleSets(source,power.SourceEffectsCancelled,power.CancelledSourceEffectTags,limit);
            var grants=new List<PendingGrant>(context.Queue.Targets);
            foreach(var effect in power.SourceEffectsApplied)AddFinalGrant(context,grants,source,effect);
            foreach(var ally in context.Power.Unit.Allies.Units)if(ally!=context.Power.Unit)CancelLifecycleSets(FindLifecycleState(context,ally),power.AllyEffectsCancelled,power.CancelledAllyEffectTags,limit);
            if(context.Power.Unit.Opposition!=null)foreach(var enemy in context.Power.Unit.Opposition.Units)CancelLifecycleSets(FindLifecycleState(context,enemy),power.EnemyEffectsCancelled,power.CancelledEnemyEffectTags,limit);
            foreach(var effect in power.AllyEffectsApplied)foreach(var ally in context.Power.Unit.Allies.Units)if(ally!=context.Power.Unit)AddFinalGrant(context,grants,FindLifecycleState(context,ally),effect);
            if(!power.TetherAllEffects||!context.Queue.Failed)
            {
                context.Queue.Committing=true;
                try{foreach(var grant in grants)grant.State.ApplyEffect(grant.Effect,grant.Source);}finally{context.Queue.Committing=false;}
                if(power.TetherAllEffects)
                {
                    var active=context.Queue.Granted.Where(e=>Get(e.State).Effects.Any(x=>x==e.Effect)).ToArray();
                    foreach(var a in active){List<EffectReference> links;if(!Get(a.State).Links.TryGetValue(a.Effect,out links)){links=new List<EffectReference>();Get(a.State).Links.Add(a.Effect,links);}foreach(var b in active)if(a.Effect!=b.Effect)links.Add(b);}
                }
            }
            if(context.Pending)pendingActions.GetValue(context.States,s=>new PendingActions()).Queues.Remove(context.Power);
        }
        static void AddFinalGrant(LifecycleContext context,List<PendingGrant> grants,Combat.CombatUnit.EstimatedState state,Effect effect)
        {
            if(CanApplyEffectForEstimate(state.Unit.Unit,effect,state))grants.Add(new PendingGrant{State=state,Effect=effect,Source=context.Power.Unit});else context.Queue.Failed=true;
        }
        static void AfterLifecycle(bool __5,LifecycleContext __state)
        {
            if(__state!=null&&__5&&__state.Queue.HadTargets)CompleteLifecycle(__state);
        }
        static Exception EndLifecycle(LifecycleContext __state,Exception __exception)
        {
            if(__state!=null)
            {
                if(__exception!=null&&__state.Pending)pendingActions.GetValue(__state.States,s=>new PendingActions()).Queues.Remove(__state.Power);
                if(lifecycleContexts!=null&&lifecycleContexts.Count>0&&lifecycleContexts.Peek()==__state)lifecycleContexts.Pop();
            }
            return __exception;
        }
        static void BeforeLifecycleReaction(Combat.CombatUnit.EstimatedState __instance,Combat.CombatUnit.EstimatedState sourceState,Combat.PowerReaction reaction,out LifecycleContext __state)
        {
            __state=null;if(!LifecycleActive||(reaction.type!=Combat.PowerReaction.ReactionType.ReturnFire&&reaction.type!=Combat.PowerReaction.ReactionType.Overwatch))return;
            var states=CurrentLifecycle==null?new Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>():CurrentLifecycle.States;
            states[__instance.Unit]=__instance;states[sourceState.Unit]=sourceState;
            __state=new LifecycleContext{States=states,Power=reaction.reactionPower,Queue=new ActionQueue{HadTargets=true}};PushLifecycle(__state);
        }
        static void AfterLifecycleReaction(LifecycleContext __state){if(__state!=null)CompleteLifecycle(__state);}
        public static bool IsGuardedForEstimate(Combat.CombatUnit.EstimatedState state,Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> states,ref List<Combat.CombatUnit> guardians,Combat.CombatUnitPower power)
        {
            return (!LifecycleActive||power.Power.Hostility!=Power.PowerHostility.Ally)&&state.IsGuarded(states,ref guardians);
        }
        static HarmonyMethod LifecycleHook(string name){return new HarmonyMethod(typeof(ProjectedEffectFix).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic));}
        internal static void ValidateLifecycleTargets()
        {
            var state=typeof(Combat.CombatUnit.EstimatedState);
            var reaction=state.GetMethod("EstimateReaction",new[]{state,typeof(Combat.PowerReaction),typeof(Combat.CombatTime)});
            if(reaction==null||reaction.ReturnType!=typeof(void)||reaction.GetMethodBody()==null)throw new InvalidDataException("Unsupported reaction lifecycle forecast signature.");
            ImmunityTranspile(PatchProcessor.GetOriginalInstructions(state.GetMethod("EstimatePower")),2).ToArray();
            ImmunityTranspile(PatchProcessor.GetOriginalInstructions(state.GetMethod("EstimateSourceEffects")),1).ToArray();
            GuardLifecycleTranspile(PatchProcessor.GetOriginalInstructions(ComboEffectFix.ResolveForecastTarget())).ToArray();
        }
        internal static void InstallLifecycle(Harmony harmony)
        {
            ValidateLifecycleTargets();var state=typeof(Combat.CombatUnit.EstimatedState);
            harmony.Patch(state.GetMethod("EstimatePower"),transpiler:LifecycleHook("PowerImmunityTranspile"));
            harmony.Patch(state.GetMethod("EstimateSourceEffects"),prefix:LifecycleHook("DeferSourceEffects"),transpiler:LifecycleHook("SourceImmunityTranspile"));
            var after=LifecycleHook("AfterLifecycle");after.priority=Priority.First;
            harmony.Patch(ComboEffectFix.ResolveForecastTarget(),prefix:LifecycleHook("BeforeLifecycle"),postfix:after,finalizer:LifecycleHook("EndLifecycle"),transpiler:LifecycleHook("GuardLifecycleTranspile"));
            harmony.Patch(state.GetMethod("EstimateReaction"),prefix:LifecycleHook("BeforeLifecycleReaction"),postfix:LifecycleHook("AfterLifecycleReaction"),finalizer:LifecycleHook("EndLifecycle"));
        }
        static IEnumerable<CodeInstruction> PowerImmunityTranspile(IEnumerable<CodeInstruction> instructions){return ImmunityTranspile(instructions,2);}
        static IEnumerable<CodeInstruction> SourceImmunityTranspile(IEnumerable<CodeInstruction> instructions){return ImmunityTranspile(instructions,1);}
        static IEnumerable<CodeInstruction> ImmunityTranspile(IEnumerable<CodeInstruction> instructions,int expected)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();var native=typeof(Unit).GetMethod("CanApplyEffect");var sites=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(native)).ToArray();
            if(sites.Length!=expected)throw new InvalidDataException("Unsupported effect immunity forecast layout.");
            for(int n=sites.Length-1;n>=0;n--){int index=sites[n];var extra=new CodeInstruction(OpCodes.Ldarg_0);extra.labels.AddRange(code[index].labels);code[index].labels.Clear();extra.blocks.AddRange(code[index].blocks);code[index].blocks.Clear();code[index].opcode=OpCodes.Call;code[index].operand=typeof(ProjectedEffectFix).GetMethod(expected==2?(n==0?"PrepareEffectForEstimate":"CanApplyTargetEffectForEstimate"):"CanApplyEffectForEstimate");code.Insert(index,extra);}return code;
        }
        static IEnumerable<CodeInstruction> GuardLifecycleTranspile(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();var native=typeof(Combat.CombatUnit.EstimatedState).GetMethod("IsGuarded");var sites=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(native)).ToArray();if(sites.Length!=1)throw new InvalidDataException("Unsupported ally guard forecast layout.");
            int index=sites[0];var extra=new CodeInstruction(OpCodes.Ldarg_2);extra.labels.AddRange(code[index].labels);code[index].labels.Clear();extra.blocks.AddRange(code[index].blocks);code[index].blocks.Clear();code[index].opcode=OpCodes.Call;code[index].operand=typeof(ProjectedEffectFix).GetMethod("IsGuardedForEstimate");code.Insert(index,extra);return code;
        }
    }
}
