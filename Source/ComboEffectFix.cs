using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mdi.SRRW.Engine;

namespace SRBugfix
{
    public static class ComboEffectFix
    {
        public static bool Enabled=false;
        const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        static readonly FieldInfo appliedEvent=typeof(Combat).GetField("OnEffectApplied",PrivateInstance);
        public static MethodInfo ResolveTarget()
        {
            var method=typeof(Combat.ExecutePowerEvent).GetMethod("CalculateResults",PrivateInstance,null,new[]{typeof(Combat.CombatPowerExecution),typeof(List<Combat.CombatPowerResult>),typeof(Dictionary<string,int>)},null);
            if(method==null||method.ReturnType!=typeof(bool)||method.GetMethodBody()==null)throw new InvalidDataException("Unsupported combo effect execution signature.");
            return method;
        }
        public static MethodInfo ResolveForecastTarget()
        {
            var method=typeof(Combat).GetMethod("EstimatePowerAgainstTargets",PrivateInstance,null,new[]{typeof(Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState>),typeof(Combat.CombatUnitPower),typeof(Dictionary<Combat.CombatUnit,Combat.UnitResults>),typeof(List<Combat.CombatUnit>),typeof(bool),typeof(bool),typeof(Action<Combat.CombatUnit>),typeof(Action<bool>)},null);
            if(method==null||method.ReturnType!=typeof(void)||method.GetMethodBody()==null)throw new InvalidDataException("Unsupported combo effect forecast signature.");
            return method;
        }
        public static void ValidateTargets()
        {
            var instructions=PatchProcessor.GetOriginalInstructions(ResolveTarget()).ToList();
            var apply=typeof(Combat.CombatPowerResult).GetMethod("ApplyEffect",new[]{typeof(Combat.CombatPowerExecution),typeof(Combat.CombatUnit),typeof(Effect)});
            var other=typeof(Combat.CombatPowerExecution).GetMethod("GetOtherUnitInCombo",new[]{typeof(Combat.CombatUnit)});
            if(appliedEvent==null||appliedEvent.FieldType!=typeof(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,Combat.CombatPowerExecution>)||other==null||other.ReturnType!=typeof(Combat.CombatUnit)||other.GetMethodBody()==null)
                throw new InvalidDataException("Unsupported combo participant or effect event context.");
            if(instructions.Count(i=>i.Calls(apply))!=1||instructions.Count(i=>i.opcode==OpCodes.Ldfld&&Equals(i.operand,appliedEvent))!=1||
                instructions.Count(i=>i.Calls(typeof(UnitPower).GetProperty("SourceEffectsApplied").GetGetMethod()))!=1||instructions.Count(i=>i.Calls(typeof(UnitPower).GetProperty("AllyEffectsApplied").GetGetMethod()))!=1)
                throw new InvalidDataException("Unsupported combo effect execution IL shape.");
            var forecast=PatchProcessor.GetOriginalInstructions(ResolveForecastTarget()).ToList();
            var against=typeof(Combat).GetMethod("EstimatePowerAgainstTarget",PrivateInstance);
            if(against==null||forecast.Count(i=>i.Calls(against))!=1||forecast.Count(i=>i.Calls(typeof(Combat).GetMethod("GetPowerReactions")))!=1)
                throw new InvalidDataException("Unsupported combo effect forecast IL shape.");
        }
        public static void Install(Harmony harmony)
        {
            ValidateTargets();
            harmony.Patch(ResolveTarget(),postfix:new HarmonyMethod(typeof(ComboEffectFix).GetMethod("AfterExecution",BindingFlags.Static|BindingFlags.NonPublic)));
            harmony.Patch(ResolveForecastTarget(),postfix:new HarmonyMethod(typeof(ComboEffectFix).GetMethod("AfterForecast",BindingFlags.Static|BindingFlags.NonPublic)));
        }
        static Effect PartnerEffect(Combat.CombatUnitPower power,Combat.CombatUnit partner)
        {
            if(!Enabled||power==null||power.Power==null||power.Power.Power.ID!="hDarSpeCombo"||!power.Power.IsComboPower||!power.Power.IsPrimaryComboUnit()||partner==null||partner==power.Unit||partner.Allies!=power.Unit.Allies||partner.State==Combat.CombatUnit.UnitState.Dead)return null;
            var combo=power.Power.Combo;
            if(combo.ComboData==null||combo.ComboData.id!="hDarSpeCombo"||combo.ComboData.primaryUnitClassID!="hSpe"||combo.ComboData.secondaryUnitClassID!="hDar"||combo.SecondaryUnitID!=partner.ID||power.Unit.Unit.UnitClassData.id!="hSpe"||partner.Unit.UnitClassData.id!="hDar")return null;
            var effect=power.Power.EffectsApplied.FirstOrDefault(e=>e.ID=="hDarSpeCombo");
            if(effect==null||effect.EffectData.requiredUnitTags==null||effect.EffectData.requiredUnitTags.Length!=1||effect.EffectData.requiredUnitTags[0]!="hDarUnit"||!partner.Unit.CanApplyEffect(effect))return null;
            return effect;
        }
        static void AfterExecution(Combat.CombatPowerExecution __0,List<Combat.CombatPowerResult> __1,bool __result)
        {
            if(!Enabled||!__result||__0==null||__0.power==null||__1==null)return;
            var partner=__0.GetOtherUnitInCombo(__0.power.Unit);var effect=PartnerEffect(__0.power,partner);if(effect==null)return;
            if(__1.Any(r=>r.type==Combat.CombatPowerResult.Type.ApplyEffect&&r.target==partner&&r.effect==effect))return;
            var notify=(Action<Combat.CombatUnit,Effect,Combat.CombatUnit,Combat.CombatPowerExecution>)appliedEvent.GetValue(__0.power.Unit.Combat);
            if(notify==null)return;
            notify(partner,effect,__0.power.Unit,__0);
            __1.Add(Combat.CombatPowerResult.ApplyEffect(__0,partner,effect));
        }
        static void AfterForecast(Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> __0,Combat.CombatUnitPower __1,Dictionary<Combat.CombatUnit,Combat.UnitResults> __2,List<Combat.CombatUnit> __3)
        {
            if(__3!=null&&__3.Count>0)CancelProjectedInstantOnUse(__0,__1);
            if(!Enabled||__1==null||__1.Power==null||__1.Power.Power.ID!="hDarSpeCombo"||__0==null||__2==null||__3==null||!__3.Contains(__1.Unit))return;
            var partner=__1.GetComboPartner();var effect=PartnerEffect(__1,partner);if(effect==null)return;
            Combat.CombatUnit.EstimatedState state;
            if(!__0.TryGetValue(partner,out state)){state=new Combat.CombatUnit.EstimatedState(partner);__0[partner]=state;}
            if(state.health<=0)return;
            state.ApplyEffect(effect,__1.Unit);
            if(!__2.ContainsKey(partner))__2[partner]=new Combat.UnitResults{unit=partner,powerReactions=new List<Combat.PowerReaction>()};
        }
        static void CancelProjectedInstantOnUse(Dictionary<Combat.CombatUnit,Combat.CombatUnit.EstimatedState> states,Combat.CombatUnitPower power)
        {
            if(!Enabled||!ProjectedEffectFix.Enabled||states==null||power==null||power.Power==null||power.Unit==null||power.Unit.Unit.UnitClassData==null||power.Unit.Unit.UnitClassData.id!="hDar")return;
            Combat.CombatUnit.EstimatedState state;if(!states.TryGetValue(power.Unit,out state))return;
            var effects=ProjectedEffectFix.EffectsForEstimate(power.Unit.Unit,state);
            foreach(var effect in effects.Where(e=>e.Effect.ID=="hDarSpeCombo"&&e.Effect.EffectData.tagsHash.Overlaps(power.Power.CancelledSourceEffectTags)).Select(e=>e.Effect).ToArray())
                ProjectedEffectFix.CancelForEstimate(state,effect);
        }
    }
}
