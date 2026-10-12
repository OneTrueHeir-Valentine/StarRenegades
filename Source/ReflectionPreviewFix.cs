using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mdi.SRRW.Engine;
namespace SRBugfix
{
    public static class ReflectionPreviewFix
    {
        internal static bool Enabled=false;
        sealed class IncomingHit
        {
            internal Combat.CombatUnitPower Power;
            internal Combat.CombatTime Time;
            internal float BaseDamage,FirstStrikeBonus,CriticalDefense;
            internal float ReflectedDamage;
            internal List<Combat.CombatUnitEffect> Overwatch,ReturnFire,Guarding;
        }
        sealed class ReflectionHit { internal float Damage; }
        sealed class ReactionLists
        {
            internal Combat.CombatUnit.EstimatedState Source,Target;
            internal List<Combat.CombatUnitEffect> Overwatch,ReturnFire,Guarding;
        }
        static readonly ConditionalWeakTable<Combat.CombatUnit.EstimatedState,IncomingHit> incoming=new ConditionalWeakTable<Combat.CombatUnit.EstimatedState,IncomingHit>();
        static readonly ConditionalWeakTable<Combat.PowerReaction,ReflectionHit> reactions=new ConditionalWeakTable<Combat.PowerReaction,ReflectionHit>();
        public static void ValidateReactionTargets()
        {
            var state=typeof(Combat.CombatUnit.EstimatedState);
            var power=state.GetMethod("EstimatePower",new[]{state,typeof(Combat.CombatUnitPower),typeof(Combat.CombatTime),typeof(bool),typeof(bool),typeof(bool).MakeByRefType(),typeof(bool),typeof(bool)});
            var reaction=state.GetMethod("EstimateReaction",new[]{state,typeof(Combat.PowerReaction),typeof(Combat.CombatTime)});
            var get=typeof(Combat).GetMethod("GetPowerReactions",new[]{typeof(Combat.CombatUnitPower),typeof(Combat.CombatTime),typeof(Combat.CombatUnit),typeof(Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>)});
            if(power==null||power.ReturnType!=typeof(void)||power.GetParameters()[5].Name!="criticalHit"||reaction==null||reaction.ReturnType!=typeof(void)||get==null||get.ReturnType!=typeof(List<Combat.PowerReaction>))throw new InvalidDataException("Supported reflection reaction forecast was not found.");
        }
        public static void InstallReactionHooks(Harmony harmony)
        {
            ValidateReactionTargets();
            var state=typeof(Combat.CombatUnit.EstimatedState);
            var power=state.GetMethod("EstimatePower");var reaction=state.GetMethod("EstimateReaction");var get=typeof(Combat).GetMethod("GetPowerReactions");
            if(power==null||reaction==null||get==null||get.ReturnType!=typeof(List<Combat.PowerReaction>))throw new InvalidDataException("Supported reflection reaction forecast was not found.");
            harmony.Patch(power,prefix:ReactionHook("BeforePower"),postfix:ReactionHook("AfterPower"));
            harmony.Patch(get,prefix:ReactionHook("BeforeReactions"),postfix:ReactionHook("AfterReactions"),finalizer:ReactionHook("RestoreReactionLists"));
            harmony.Patch(reaction,prefix:ReactionHook("EstimateReflection"));
        }
        static HarmonyMethod ReactionHook(string name){return new HarmonyMethod(typeof(ReflectionPreviewFix).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic));}
        static void BeforePower(Combat.CombatUnit.EstimatedState __instance,Combat.CombatUnit.EstimatedState sourceState,Combat.CombatUnitPower power,Combat.CombatTime executionTime,out IncomingHit __state)
        {
            __state=null;if(!Enabled)return;
            float damage=power.Unit.Unit.Damage*power.Power.DamageMin*.01f;
            __state=new IncomingHit{Power=power,Time=executionTime,BaseDamage=damage,CriticalDefense=ProjectedEffectFix.CriticalDefenseForEstimate(__instance.Unit.Unit,__instance),FirstStrikeBonus=!power.Power.NoFirstStrike&&__instance.susceptibleToFirstStrike?damage*__instance.Unit.Game.Rules.firstStrikeDamageBonus*.01f:0,Overwatch=new List<Combat.CombatUnitEffect>(sourceState.overwatchEffects),ReturnFire=new List<Combat.CombatUnitEffect>(__instance.returnFireEffects),Guarding=new List<Combat.CombatUnitEffect>(__instance.guardingEffects)};
        }
        static void AfterPower(Combat.CombatUnit.EstimatedState __instance,bool criticalHit,IncomingHit __state)
        {
            if(!Enabled||__state==null)return;
            float damage=__state.BaseDamage;
            if(criticalHit)damage+=damage*__state.Power.Power.CriticalDamageBonusPercent*.01f*(1-__state.CriticalDefense*.01f);
            // Execution reflects before vulnerability, defense and reflect reduction, and adds first strike after CRIT.
            __state.ReflectedDamage=(damage+__state.FirstStrikeBonus)*__instance.Unit.Unit.ReflectDamage*.01f;
            incoming.Remove(__instance);incoming.Add(__instance,__state);
        }
        static void BeforeReactions(Combat.CombatUnitPower power,Combat.CombatTime executionTime,Combat.CombatUnit target,Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> estimatedUnitStates,out ReactionLists __state)
        {
            __state=null;IncomingHit hit;
            if(!Enabled||!incoming.TryGetValue(estimatedUnitStates[target],out hit)||hit.Power!=power||hit.Time!=executionTime)return;
            var source=estimatedUnitStates[power.Unit];var targetState=estimatedUnitStates[target];
            __state=new ReactionLists{Source=source,Target=targetState,Overwatch=source.overwatchEffects,ReturnFire=targetState.returnFireEffects,Guarding=targetState.guardingEffects};
            source.overwatchEffects=hit.Overwatch;targetState.returnFireEffects=hit.ReturnFire;targetState.guardingEffects=hit.Guarding;
        }
        static Exception RestoreReactionLists(ReactionLists __state,Exception __exception)
        {
            if(__state!=null){__state.Source.overwatchEffects=__state.Overwatch;__state.Target.returnFireEffects=__state.ReturnFire;__state.Target.guardingEffects=__state.Guarding;}
            return __exception;
        }
        static void AfterReactions(Combat.CombatUnitPower power,Combat.CombatTime executionTime,Combat.CombatUnit target,Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> estimatedUnitStates,List<Combat.PowerReaction> __result)
        {
            if(!Enabled)return;
            IncomingHit hit;
            if(!incoming.TryGetValue(estimatedUnitStates[target],out hit)||hit.Power!=power||hit.Time!=executionTime)return;
            foreach(var reaction in __result.Where(r=>r.type==Combat.PowerReaction.ReactionType.ReflectDamage))
            {
                reactions.Add(reaction,new ReflectionHit{Damage=hit.ReflectedDamage});
                var estimate=new Combat.CombatUnit.EstimatedState(power.Unit);
                estimate.EstimateDamage(estimatedUnitStates[target],power,executionTime,hit.ReflectedDamage,false,true,false,true);
                reaction.estimatedResults=new Combat.CombatUnitPower.EstimatedResults(estimate);
            }
        }
        static bool EstimateReflection(Combat.CombatUnit.EstimatedState __instance,Combat.CombatUnit.EstimatedState sourceState,Combat.PowerReaction reaction,Combat.CombatTime executionTime)
        {
            ReflectionHit hit;
            if(!Enabled||reaction.type!=Combat.PowerReaction.ReactionType.ReflectDamage||!reactions.TryGetValue(reaction,out hit))return true;
            // Each GetPowerReactions result belongs to one hit; the outer scenario already iterates NumAttacks.
            __instance.EstimateDamage(sourceState,reaction.power,executionTime,hit.Damage,false,true,false,true);
            return false;
        }
        public static MethodInfo ResolveTarget()
        {
            var method=typeof(Combat.CombatUnit.EstimatedState).GetMethod("EstimateDamage",new[]{typeof(Combat.CombatUnit.EstimatedState),typeof(Combat.CombatUnitPower),typeof(Combat.CombatTime),typeof(float),typeof(bool),typeof(bool),typeof(bool),typeof(bool)});
            if(method==null||method.ReturnType!=typeof(void)||method.GetParameters()[5].Name!="isReflectDamage")throw new InvalidDataException("Supported damage forecast was not found.");
            return method;
        }
        public static float ArmorDamageForEstimate(UnitPower power,bool isReflectDamage){return Enabled&&isReflectDamage?0:power.ArmorDamageMin;}
        public static int DelayForEstimate(float value){return Enabled?(int)value:UnityEngine.Mathf.RoundToInt(value);}
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
            var armor=typeof(UnitPower).GetProperty("ArmorDamageMin").GetGetMethod();
            var defense=typeof(UnitPower).GetProperty("TimeDamageDefense").GetGetMethod();
            var round=typeof(UnityEngine.Mathf).GetMethod("RoundToInt",new[]{typeof(float)});
            int[] armorSites=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(armor)).ToArray();
            int[] defenseSites=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(defense)).ToArray();
            if(armorSites.Length!=1||defenseSites.Length!=1)throw new InvalidDataException("Unsupported damage forecast layout.");
            int delay=-1;
            for(int i=defenseSites[0]+1;i<Math.Min(code.Count,defenseSites[0]+12);i++)
            {
                if(code[i].Calls(round)){delay=i;break;}
                if(code[i].opcode.FlowControl==FlowControl.Call||code[i].opcode.FlowControl==FlowControl.Branch||code[i].opcode.FlowControl==FlowControl.Cond_Branch)break;
            }
            if(delay<0)throw new InvalidDataException("Supported delay forecast was not found.");
            code[delay].opcode=OpCodes.Call;code[delay].operand=typeof(ReflectionPreviewFix).GetMethod("DelayForEstimate");
            int at=armorSites[0];var arg=new CodeInstruction(OpCodes.Ldarg_S,(byte)6);
            arg.labels.AddRange(code[at].labels);code[at].labels.Clear();
            arg.blocks.AddRange(code[at].blocks);code[at].blocks.Clear();
            code[at].opcode=OpCodes.Call;code[at].operand=typeof(ReflectionPreviewFix).GetMethod("ArmorDamageForEstimate");code.Insert(at,arg);
            return code;
        }
    }
}
