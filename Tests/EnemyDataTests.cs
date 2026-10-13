using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;

internal static class EnemyDataTests
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int passed,failed;
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);
            if(!File.Exists(file))file=Path.Combine(args[0],"Star Renegades_Data/Managed",name);
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Test("StagingLeavesOriginalActionDictionaryAndArraysUntouched",()=>{
            var data=Fixture();var original=data.UnitCombatActionData;var array=original["oVanSummonA"].nextAction;
            Stage(data);Check(ReferenceEquals(data.UnitCombatActionData,original),"staging replaced live dictionary");
            Check(ReferenceEquals(array,original["oVanSummonA"].nextAction)&&array[0]=="oVanBSummonB","staging changed original action");
        });
        Test("DragoonContinuationResolvesDefinedSummonBActionAfterCommit",()=>{
            var data=Fixture();var game=Blank<SRRWGame>();Set(game,"gameData",data);
            Commit(Stage(data),data);
            string continuation=data.UnitCombatActionData["oVanSummonA"].nextAction[0];
            Check(continuation=="oVanSummonB"&&game.GetUnitCombatAction(continuation).powerIDs[0]=="oVanBSummonB","Dragoon B summon continuation still fails");
        });
        Test("PanzerRepairRetainsAllThreeDefinedAttackChoices",()=>{
            var data=Fixture();Commit(Stage(data),data);var next=data.UnitCombatActionData["ePanActRepair"].nextAction;
            Check(string.Join("|",next)=="ePanActScattershot|ePanActFlurry|ePanActAoeAttack","Panzer repair choices differ");
            foreach(string id in next)Check(data.UnitCombatActionData.ContainsKey(id),"Panzer continuation still missing");
        });
        Test("CommitClonesOnlyEditedActionRecords",()=>{
            var data=Fixture();var original=data.UnitCombatActionData;Commit(Stage(data),data);
            Check(!ReferenceEquals(original["oVanSummonA"],data.UnitCombatActionData["oVanSummonA"]),"Dragoon row reused");
            Check(!ReferenceEquals(original["ePanActRepair"],data.UnitCombatActionData["ePanActRepair"]),"Panzer row reused");
            Check(ReferenceEquals(original["oVanSummonB"],data.UnitCombatActionData["oVanSummonB"]),"unedited row cloned");
            Check(original["ePanActRepair"].nextAction[2]=="ePanActAoeFlurry","original Panzer row changed");
        });
        Test("RestoreReturnsExactOriginalDictionary",()=>{
            var data=Fixture();var original=data.UnitCombatActionData;var stage=Stage(data);Commit(stage,data);Invoke(stage,"Restore",data);
            Check(ReferenceEquals(original,data.UnitCombatActionData),"restore lost original dictionary");
        });
        Test("ChangedBeforeValueRejectsEntireStage",()=>{
            var data=Fixture();data.UnitCombatActionData["ePanActRepair"].nextAction[2]="different";var original=data.UnitCombatActionData;
            Reject(()=>Stage(data));Check(ReferenceEquals(data.UnitCombatActionData,original)&&original["oVanSummonA"].nextAction[0]=="oVanBSummonB","rejected stage changed other row");
        });
        Test("MissingReplacementActionRejectsStage",()=>{
            var data=Fixture();data.UnitCombatActionData.Remove("oVanSummonB");Reject(()=>Stage(data));
        });
        Test("MissingEditedActionRejectsStage",()=>{
            var data=Fixture();data.UnitCombatActionData.Remove("ePanActRepair");Reject(()=>Stage(data));
        });
        Test("ChangedDictionaryBetweenStageAndCommitIsPreserved",()=>{
            var data=Fixture();var stage=Stage(data);var replacement=new Dictionary<string,UnitCombatActionData>(data.UnitCombatActionData);
            typeof(GameData).GetProperty("UnitCombatActionData").GetSetMethod(true).Invoke(data,new object[]{replacement});
            Reject(()=>Commit(stage,data));Check(ReferenceEquals(replacement,data.UnitCombatActionData),"commit overwrote a concurrent dictionary");
        });
        Test("UncertainBountyHunterFallbackIsPreserved",()=>{
            var data=Fixture();var original=data.UnitCombatActionData["eBhuT1M1P3"];Commit(Stage(data),data);
            Check(ReferenceEquals(original,data.UnitCombatActionData["eBhuT1M1P3"])&&original.ifUnusableAction[1]=="eBhuT1M1P3 ","held Bounty Hunter behavior changed");
        });
        Test("ChangedFieldsReportsTwoEdits",()=>{
            var stage=Stage(Fixture());Check((int)stage.GetType().GetField("ChangedFields").GetValue(stage)==2,"incorrect edited-field count");
        });
        Test("MissingActionDictionaryIsRejected",()=>Reject(()=>Stage(Blank<GameData>())));
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static GameData Fixture()
    {
        var data=Blank<GameData>();var actions=new Dictionary<string,UnitCombatActionData>{
            {"oVanSummonA",new UnitCombatActionData{id="oVanSummonA",nextAction=new[]{"oVanBSummonB"},powerIDs=new[]{"oVanBSummonA"}}},
            {"oVanSummonB",new UnitCombatActionData{id="oVanSummonB",powerIDs=new[]{"oVanBSummonB"}}},
            {"ePanActRepair",new UnitCombatActionData{id="ePanActRepair",nextAction=new[]{"ePanActScattershot","ePanActFlurry","ePanActAoeFlurry"}}},
            {"ePanActScattershot",new UnitCombatActionData{id="ePanActScattershot"}},
            {"ePanActFlurry",new UnitCombatActionData{id="ePanActFlurry"}},
            {"ePanActAoeAttack",new UnitCombatActionData{id="ePanActAoeAttack",powerIDs=new[]{"ePanAoeFlurry"}}},
            {"eBhuT1M1P3",new UnitCombatActionData{id="eBhuT1M1P3",ifUnusableAction=new[]{"eBhuT1M1P2","eBhuT1M1P3 ","eBhuT1M1P4"}}}
        };
        typeof(GameData).GetProperty("UnitCombatActionData").GetSetMethod(true).Invoke(data,new object[]{actions});return data;
    }
    static object Stage(GameData data){var type=typeof(EnemyDataTests).Assembly.GetType("SRBugfix.EnemyDataFix");Check(type!=null,"approved enemy data corrections are not implemented");return Invoke(type,"Stage",data);}
    static void Commit(object stage,GameData data){Invoke(stage,"Commit",data);}
    static object Invoke(object target,string name,params object[] args){try{return (target is Type?(Type)target:target.GetType()).GetMethod(name,Flags).Invoke(target is Type?null:target,args);}catch(TargetInvocationException error){throw error.InnerException;}}
    static T Blank<T>(){return (T)FormatterServices.GetUninitializedObject(typeof(T));}
    static void Set(object target,string name,object value){for(var type=target.GetType();type!=null;type=type.BaseType){var field=type.GetField(name,Flags|BindingFlags.DeclaredOnly);if(field!=null){field.SetValue(target,value);return;}}throw new Exception("missing field "+name);}
    static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("invalid stage was accepted");}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
}
