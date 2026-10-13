using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mdi.SRRW.Data;

namespace SRBugfix
{
    public static class EnemyDataFix
    {
        static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        public sealed class StageResult
        {
            public int ChangedFields;
            readonly GameData owner;
            readonly Dictionary<string,UnitCombatActionData> original,replacement;
            internal StageResult(GameData data,Dictionary<string,UnitCombatActionData> before,Dictionary<string,UnitCombatActionData> after)
            {owner=data;original=before;replacement=after;ChangedFields=2;}
            public void Commit(GameData data)
            {
                if(!ReferenceEquals(data,owner)||!ReferenceEquals(data.UnitCombatActionData,original))throw new InvalidDataException("Enemy actions changed since staging.");
                Set(data,replacement);
            }
            public void Restore(GameData data)
            {
                if(!ReferenceEquals(data,owner))throw new InvalidDataException("Enemy action rollback belongs to a different data instance.");
                Set(data,original);
            }
            static void Set(GameData data,Dictionary<string,UnitCombatActionData> value)
            {typeof(GameData).GetProperty("UnitCombatActionData").GetSetMethod(true).Invoke(data,new object[]{value});}
        }
        public static StageResult Stage(GameData data)
        {
            if(data==null||data.UnitCombatActionData==null)throw new InvalidDataException("Enemy action data is missing.");
            var original=data.UnitCombatActionData;
            foreach(string id in new[]{"oVanSummonB","ePanActScattershot","ePanActFlurry","ePanActAoeAttack"})
                if(!original.ContainsKey(id))throw new InvalidDataException("Enemy continuation action is missing: "+id);
            var replacement=new Dictionary<string,UnitCombatActionData>(original);
            Correct(original,replacement,"oVanSummonA",new[]{"oVanBSummonB"},new[]{"oVanSummonB"});
            Correct(original,replacement,"ePanActRepair",new[]{"ePanActScattershot","ePanActFlurry","ePanActAoeFlurry"},new[]{"ePanActScattershot","ePanActFlurry","ePanActAoeAttack"});
            return new StageResult(data,original,replacement);
        }
        static void Correct(Dictionary<string,UnitCombatActionData> original,Dictionary<string,UnitCombatActionData> replacement,string id,string[] before,string[] after)
        {
            UnitCombatActionData row;
            if(!original.TryGetValue(id,out row)||row==null||row.nextAction==null||!row.nextAction.SequenceEqual(before))throw new InvalidDataException("Enemy action baseline differs: "+id);
            var copy=(UnitCombatActionData)Clone.Invoke(row,null);copy.nextAction=(string[])after.Clone();replacement[id]=copy;
        }
    }
}
