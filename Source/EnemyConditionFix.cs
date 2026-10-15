using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;

namespace SRBugfix
{
    public static class EnemyConditionFix
    {
        public static bool Enabled=false;
        const string NativeBodySha256="479c8f8adf4d122314a9bfa86b39ffd1740d30f6225a61d5a26e30de8383e6f5";

        public static MethodInfo ResolveTarget()
        {
            var method=typeof(Combat.CombatAI).GetMethod("DoesUnitSatisfyIfEnergyAction",
                BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly,
                null,new[]{typeof(Combat.CombatUnit),typeof(UnitCombatActionData)},null);
            if(method==null||!method.IsPrivate||method.IsStatic||method.ReturnType!=typeof(bool)||method.GetMethodBody()==null)
                throw new InvalidDataException("Unsupported enemy AI energy condition signature.");
            return method;
        }

        public static void ValidateTargets()
        {
            byte[] body=ResolveTarget().GetMethodBody().GetILAsByteArray();
            string actual;
            using(var hash=SHA256.Create())actual=BitConverter.ToString(hash.ComputeHash(body)).Replace("-","").ToLowerInvariant();
            if(actual!=NativeBodySha256)throw new InvalidDataException("Unsupported enemy AI energy condition body.");
        }

        public static void Install(Harmony harmony)
        {
            ValidateTargets();
            harmony.Patch(ResolveTarget(),prefix:new HarmonyMethod(typeof(EnemyConditionFix).GetMethod("EnergyConditionPrefix",BindingFlags.Static|BindingFlags.NonPublic)));
        }

        static bool EnergyConditionPrefix(Combat.CombatUnit __0,UnitCombatActionData __1,ref bool __result)
        {
            if(!Enabled)return true;
            float current=__0.Unit.CurrentEnergy;
            float maximum=__0.Unit.MaximumEnergy;
            float threshold=__1.ifEnergyPercent;
            if(maximum<=0f||!Finite(current)||!Finite(maximum)||!Finite(threshold))return true;

            // Compare the authored percentages without first rounding a float
            // ratio. At 30/50, that ratio otherwise becomes 60.0000038 percent.
            double amount=(double)current*100d;
            double limit=(double)threshold*(double)maximum;
            switch(__1.IfEnergyCondition){
                case UnitCombatActionData.EnergyCondition.LessThan:__result=amount<limit;break;
                case UnitCombatActionData.EnergyCondition.LessThanEqualTo:__result=amount<=limit;break;
                case UnitCombatActionData.EnergyCondition.EqualTo:__result=amount==limit;break;
                case UnitCombatActionData.EnergyCondition.GreaterThanEqualTo:__result=amount>=limit;break;
                case UnitCombatActionData.EnergyCondition.GreaterThan:__result=amount>limit;break;
                default:__result=false;break;
            }
            return false;
        }

        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
