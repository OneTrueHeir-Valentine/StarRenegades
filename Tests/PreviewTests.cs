using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Newtonsoft.Json.Linq;
using SRBugfix;

internal static class PreviewTests
{
    static int passed,failed;
    static string sourceRoot,gameRoot;
    static int Main(string[] args)
    {
        gameRoot=args[0];sourceRoot=args[1];
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);
            if(!File.Exists(path))path=Path.Combine(gameRoot,"Star Renegades_Data/Managed/"+name);
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Test("IndividualTargetUsesRankThreshold",()=>{
            var game=Game();int[] amounts={0,25,50,75,100};
            for(int i=0;i<amounts.Length;i++){
                var card=new CamaraderieCardData{targetType="AnyOther",requiredAffection=amounts[i]};
                Check(PreviewFix.CorrectLabel(game,card,"old")=="camaraderieCardTargetType"+(i+1),"wrong rank at "+amounts[i]);
            }
        });
        Test("AllPositiveRequirementDefinitionsMatchCamp",()=>{
            var cases=JArray.Parse(File.ReadAllText(Path.Combine(sourceRoot,"../Tests/card-preview-cases.json")));int granted=0;
            foreach(var c in cases){int amount=(int)c["requiredAffection"];var card=new CamaraderieCardData{targetType=(string)c["targetType"],requiredAffection=amount};Check(PreviewFix.CorrectLabel(Game(),card,"wrong")==Mdi.SRRW.UI.UIStrings.GetRelationshipTargetDescription(Game(),amount),"wrong rank for "+c["id"]);if((bool)c["normallyGranted"])granted++;}
            Check(cases.Count==30&&granted==17,"card coverage changed");
        });
        Test("GroupAndSelfLabelsUnchanged",()=>{
            foreach(string target in new[]{"All","AllOther","Self"})Check(PreviewFix.CorrectLabel(Game(),new CamaraderieCardData{targetType=target,requiredAffection=50},"keep")=="keep",target+" label changed");
        });
        Test("RealGetterTargetsResolve",()=>{
            var targets=PreviewFix.ResolveTargets();Check(targets.Length==2&&targets[0]!=targets[1],"expected two distinct getters");
        });
        Test("MissingRewardGetterRejectsActivation",()=>{
            bool rejected=false;try{PreviewFix.ResolveTargets(typeof(Mdi.SRRW.Engine.Camaraderie.CamaraderieCard),typeof(object));}catch(InvalidDataException){rejected=true;}Check(rejected,"missing getter accepted");
        });
        Test("SecondHookFailureRestoresCoreAndFirstHook",()=>{
            var m=JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot,"fixes.json")));var d=CoreTests.Fixture(m);
            var eq=d.EquipmentData;var traits=d.TraitData;var groups=d.TagGroupData;bool firstHook=false,rejected=false;
            try{Activation.Apply(d,m,new Action[]{()=>firstHook=true,()=>{throw new InvalidOperationException("injected hook failure");}},()=>firstHook=false);}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&!firstHook,"partial hooks remained");Check(Object.ReferenceEquals(eq,d.EquipmentData)&&Object.ReferenceEquals(traits,d.TraitData)&&Object.ReferenceEquals(groups,d.TagGroupData),"partial overlay remained");
        });
        Test("SuccessfulActivationCommitsCoreBeforeHooks",()=>{
            var m=JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot,"fixes.json")));var d=CoreTests.Fixture(m);bool hook=false;
            Activation.Apply(d,m,new Action[]{()=>{hook=true;Check(d.PowerModifierData["camParTrait1"].setCriticalTransferShieldAbsolute==0,"hook ran before data correction");}},()=>{});
            Check(hook&&d.TagGroupData.Count==48,"activation did not finish");
        });
        Test("RepeatedLoadCallbackDoesNotReapply",()=>{
            var m=JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot,"fixes.json")));var d=CoreTests.Fixture(m);int hooks=0;
            Type entry=typeof(Doorstop.Entrypoint);var flags=BindingFlags.Static|BindingFlags.NonPublic;
            entry.GetField("manifest",flags).SetValue(null,m);entry.GetField("installHooks",flags).SetValue(null,new Action[]{()=>hooks++});entry.GetField("removeHooks",flags).SetValue(null,new Action(()=>{}));entry.GetField("applications",flags).SetValue(null,0);entry.GetField("root",flags).SetValue(null,AppDomain.CurrentDomain.BaseDirectory);
            var callback=entry.GetMethod("AfterDataLoad",flags);callback.Invoke(null,new object[]{d});var applied=d.EquipmentData;callback.Invoke(null,new object[]{d});
            Check(hooks==1&&Object.ReferenceEquals(applied,d.EquipmentData),"callback reapplied data or hooks");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static SRRWGame Game()
    {
        var data=(GameData)FormatterServices.GetUninitializedObject(typeof(GameData));
        typeof(GameData).GetProperty("UIStringData").GetSetMethod(true).Invoke(data,new object[]{new Dictionary<string,LocalizedStringData>()});
        var game=(SRRWGame)FormatterServices.GetUninitializedObject(typeof(SRRWGame));
        typeof(SRRWGame).GetField("gameData",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,data);return game;
    }
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
