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
    public static class ReflectionPreviewFix
    {
        internal static bool Enabled=false;
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
