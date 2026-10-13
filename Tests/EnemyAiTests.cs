using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
#if !NATIVE_BASELINE
using SRBugfix;
#endif

// Invoke the actual private native selector. Only the game's integer RNG is
// intercepted, in this standalone process, to exercise exact decision boundaries.
internal static class EnemyAiTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int passed,failed,roll;
    static readonly List<int[]> ranges=new List<int[]>();
    static Combat.CombatAI ai;
    static MethodInfo selector;
    static Dictionary<string,UnitCombatActionData> data;

    static int Main(string[] args)
    {
        string managed=Path.Combine(args[0],"Star Renegades_Data/Managed");
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string file=Path.Combine(managed,new AssemblyName(e.Name).Name+".dll");
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        return Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        // Minimal, hand-checked vanilla rows. The real game dictionary and
        // HasUnitCombatAction lookup remain in use, without exported data files.
        data=new Dictionary<string,UnitCombatActionData>();
        foreach(string id in new[]{"eFirM1P1","eFirM1P4","bDemBuff3","bDemBuff5"})
            data[id]=new UnitCombatActionData{id=id};
        data["eFirM1P1b"]=new UnitCombatActionData{id="eFirM1P1b",nextAction=new[]{"75","eFirM1P1","25","eFirM1P4"},ifInterruptedAction=new[]{"75","eFirM1P1","25","eFirM1P4"}};
        foreach(string id in new[]{"bDemBuff2","bDemShields1","bDemCover1b"})
            data[id]=new UnitCombatActionData{id=id,nextAction=new[]{"33","bDemBuff3","66","bDemBuff5"}};
        var gameData=Blank<GameData>();
        typeof(GameData).GetProperty("UnitCombatActionData").GetSetMethod(true).Invoke(gameData,new object[]{data});
        var game=Blank<SRRWGame>();Set(game,"gameData",gameData);
        ai=Blank<Combat.CombatAI>();Set(ai,"game",game);
        Set(ai,"selectableActions",new List<string>());Set(ai,"candidateActions",new List<string>());Set(ai,"candidateProbabilities",new List<int>());
        selector=typeof(Combat.CombatAI).GetMethod("SelectAction",Flags,null,new[]{typeof(string[]),typeof(string).MakeByRefType()},null);
        var harmony=new Harmony("test.srbugfix.enemy-ai");
        try{
            var random=typeof(Mdi.SRRW.Engine.Random).GetMethod("Range",Flags,null,new[]{typeof(int),typeof(int)},null);
            harmony.Patch(random,prefix:new HarmonyMethod(typeof(EnemyAiTests).GetMethod("RangePrefix",Flags)));
#if !NATIVE_BASELINE
            Test("ExactNativeTargetValidatedBeforeInstallation",()=>{
                Check(!EnemyAiFix.Enabled,"repair enabled before activation");
                Check(EnemyAiFix.ResolveTarget()==selector,"wrong native target");EnemyAiFix.ValidateTargets();EnemyAiFix.Install(harmony);
            });
#endif
            // These controls catch an ungated hook or a change to the ordinary native path.
            Test("DisabledFirewulfRemainsUniformFirstChoice",()=>Select(data["eFirM1P1b"].nextAction,0,true,"eFirM1P1",2));
            Test("DisabledFirewulfRemainsUniformSecondChoice",()=>Select(data["eFirM1P1b"].nextAction,1,true,"eFirM1P4",2));
            Test("DisabledDemolisherRemainsUniform",()=>Select(data["bDemBuff2"].nextAction,1,true,"bDemBuff5",2));
#if !NATIVE_BASELINE
            EnemyAiFix.Enabled=true;
#endif
            // Losing weights, using <=, or changing RNG bounds breaks these cases.
            Test("FirewulfRoll0UsesFirst75Percent",()=>Select(data["eFirM1P1b"].nextAction,0,true,"eFirM1P1",100));
            Test("FirewulfRoll74UsesFirst75Percent",()=>Select(data["eFirM1P1b"].nextAction,74,true,"eFirM1P1",100));
            Test("FirewulfRoll75UsesLast25Percent",()=>Select(data["eFirM1P1b"].nextAction,75,true,"eFirM1P4",100));
            Test("FirewulfRoll99UsesLast25Percent",()=>Select(data["eFirM1P1b"].nextAction,99,true,"eFirM1P4",100));
            Test("InterruptedFirewulfRoll74UsesWeights",()=>Select(data["eFirM1P1b"].ifInterruptedAction,74,true,"eFirM1P1",100));
            Test("InterruptedFirewulfRoll75UsesWeights",()=>Select(data["eFirM1P1b"].ifInterruptedAction,75,true,"eFirM1P4",100));
            foreach(string id in new[]{"bDemBuff2","bDemShields1","bDemCover1b"}){
                string captured=id;
                Test(captured+"Roll32UsesFirst33Percent",()=>Select(data[captured].nextAction,32,true,"bDemBuff3",100));
                Test(captured+"Roll33UsesNext66Percent",()=>Select(data[captured].nextAction,33,true,"bDemBuff5",100));
                Test(captured+"Roll98UsesNext66Percent",()=>Select(data[captured].nextAction,98,true,"bDemBuff5",100));
                Test(captured+"Roll99PreservesNoChoice",()=>Select(data[captured].nextAction,99,false,null,100));
            }
            Test("SumOver100Roll119UsesFirstWeight",()=>Select(new[]{"120","eFirM1P1","80","eFirM1P4"},119,true,"eFirM1P1",200));
            Test("SumOver100Roll120UsesSecondWeight",()=>Select(new[]{"120","eFirM1P1","80","eFirM1P4"},120,true,"eFirM1P4",200));
            Test("SumOver100Roll199UsesSecondWeight",()=>Select(new[]{"120","eFirM1P1","80","eFirM1P4"},199,true,"eFirM1P4",200));
            Test("ZeroFirstWeightCannotSelectFirstAction",()=>Select(new[]{"0","eFirM1P1","100","eFirM1P4"},0,true,"eFirM1P4",100));
            Test("AllZeroWeightsProduceNoChoice",()=>Select(new[]{"0","eFirM1P1","0","eFirM1P4"},0,false,null,100));
            Test("SingleWeightedPairUsesPercentageGap",()=>Select(new[]{"25","eFirM1P1"},25,false,null,100));
            Test("SingleWeightedPairCanSelectAction",()=>Select(new[]{"25","eFirM1P1"},24,true,"eFirM1P1",100));
            // Malformed inputs must remain native, including exact ID lookup and RNG.
            Test("OrdinaryListRetainsNativeFirstChoice",()=>Select(new[]{"eFirM1P1","eFirM1P4"},0,true,"eFirM1P1",2));
            Test("OrdinaryListRetainsNativeSecondChoice",()=>Select(new[]{"eFirM1P1","eFirM1P4"},1,true,"eFirM1P4",2));
            Test("SingleOrdinaryChoiceRetainsNativeRandomCall",()=>Select(new[]{"eFirM1P1"},0,true,"eFirM1P1",1));
            Test("NegativeWeightFallsThroughToNative",()=>Select(new[]{"-1","eFirM1P1","101","eFirM1P4"},1,true,"eFirM1P4",2));
            Test("NonIntegerWeightFallsThroughToNative",()=>Select(new[]{"75.5","eFirM1P1","25","eFirM1P4"},1,true,"eFirM1P4",2));
            Test("OddLengthFallsThroughToNative",()=>Select(new[]{"75","eFirM1P1","25","eFirM1P4","trailing"},1,true,"eFirM1P4",2));
            Test("WeightSumOverflowFallsThroughToNative",()=>Select(new[]{"2147483647","eFirM1P1","1","eFirM1P4"},1,true,"eFirM1P4",2));
            Test("IntegerOverflowFallsThroughToNative",()=>Select(new[]{"2147483648","eFirM1P1","25","eFirM1P4"},1,true,"eFirM1P4",2));
            Test("MissingActionFallsThroughToNative",()=>Select(new[]{"75","missing-action","25","eFirM1P4"},0,true,"eFirM1P4",1));
            Test("ActionWhitespaceIsNotTrimmed",()=>Select(new[]{"75","eFirM1P1 ","25","eFirM1P4"},0,true,"eFirM1P4",1));
            Test("UnknownOrdinaryActionConsumesNoRandomness",()=>Select(new[]{"missing-action"},0,false,null,0));
            Test("EmptyListConsumesNoRandomness",()=>Select(new string[0],0,false,null,0));
#if !NATIVE_BASELINE
            EnemyAiFix.Enabled=false;
            Test("DisablingInstalledHookRestoresNativeUniformChoice",()=>Select(data["eFirM1P1b"].nextAction,1,true,"eFirM1P4",2));
#endif
        }finally{
#if !NATIVE_BASELINE
            EnemyAiFix.Enabled=false;
#endif
            harmony.UnpatchAll(harmony.Id);
        }
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed; native selector exercised in isolated process");
        return failed==0?0:1;
    }
    static bool RangePrefix(int __0,int __1,ref int __result)
    {
        ranges.Add(new[]{__0,__1});
        // For RED, constrain a percentage roll to the native uniform index range
        // so a failed behavior assertion, rather than an index exception, explains it.
        __result=Math.Min(roll,__1-1);return false;
    }
    static void Select(string[] input,int deterministicRoll,bool expectedResult,string expectedAction,int expectedMaximum)
    {
        roll=deterministicRoll;ranges.Clear();
        var args=new object[]{input,null};bool result=(bool)selector.Invoke(ai,args);
        Check(result==expectedResult,"expected result "+expectedResult+", got "+result);
        Check((string)args[1]==expectedAction,"expected action "+(expectedAction??"<null>")+", got "+(args[1]??"<null>"));
        Check(ranges.Count==(expectedMaximum==0?0:1),"wrong number of random calls: "+ranges.Count);
        if(expectedMaximum!=0)Check(ranges[0][0]==0&&ranges[0][1]==expectedMaximum,"expected RNG [0,"+expectedMaximum+"), got ["+ranges[0][0]+","+ranges[0][1]+")");
    }
    static T Blank<T>(){return (T)FormatterServices.GetUninitializedObject(typeof(T));}
    static void Set(object target,string name,object value)
    {
        for(var type=target.GetType();type!=null;type=type.BaseType){var field=type.GetField(name,Flags|BindingFlags.DeclaredOnly);if(field!=null){field.SetValue(target,value);return;}}
        throw new Exception("Missing field: "+name);
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
}
