using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mdi.SRRW.Engine;
namespace SRBugfix
{
    public static class DamageInspectionFix
    {
        internal static bool Enabled=false;
        public static MethodInfo ResolveTarget()
        {
            var type=typeof(Combat).Assembly.GetType("Mdi.SRRW.UI.CombatController+ScheduleUnitsState+ScheduleUnitsSubstate");
            var method=type==null?null:type.GetMethod("ShowDamagePreview",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(Combat.CombatUnit),typeof(Combat.CombatUnitPower),typeof(Combat.CombatTime),typeof(Combat.CombatUnitPower.TargetGroup)},null);
            if(method==null||method.ReturnType!=typeof(void))throw new InvalidDataException("Supported inspection preview was not found.");return method;
        }
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
            var native=typeof(Combat.CombatUnitPower).GetMethod("EstimateResultsAgainstTarget",new[]{typeof(Combat.CombatUnit),typeof(Combat.CombatTime)});
            var sites=code.Where(c=>c.Calls(native)).ToArray();
            if(sites.Length!=1)throw new InvalidDataException("Unsupported inspection preview layout.");
            sites[0].opcode=OpCodes.Call;sites[0].operand=typeof(DamageInspectionFix).GetMethod("EstimateForHud");return code;
        }
        public static Combat.CombatUnitPower.EstimatedResults EstimateForHud(Combat.CombatUnitPower power,Combat.CombatUnit target,Combat.CombatTime executionTime)
        {
            if(!Enabled)return power.EstimateResultsAgainstTarget(target,executionTime);
            var state=new Combat.CombatUnit.EstimatedState(target);
            state.EstimatePowerWithNumAttacks(new Combat.CombatUnit.EstimatedState(power.Unit),power,executionTime,false,false,out var ignored,false,state.hasPowerExecution&&state.powerExecutionTime<executionTime);
            return SignedResult(state);
        }
        public static Combat.CombatUnitPower.EstimatedResults SignedResult(Combat.CombatUnit.EstimatedState state)
        {
            var unit=state.Unit;var result=new Combat.CombatUnitPower.EstimatedResults(state);
            result.healthDelta=state.health-unit.Unit.CurrentHealth;result.armorDelta=state.armor-unit.Unit.CurrentArmor;
            result.shieldDelta=state.shield-unit.Unit.CurrentShield;result.shieldCapDelta=(float)state.shieldCap-unit.Unit.ShieldCap;
            result.energyDelta=state.energy-unit.Unit.CurrentEnergy;result.staggerDelta=state.currentStagger-unit.CurrentStagger;
            result.timeDelta=-result.timeDelta;result.interruptThresholdDelta=-result.interruptThresholdDelta;return result;
        }
    }
}
