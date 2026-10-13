using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Newtonsoft.Json.Linq;
using SRRebalance;

internal static class RebalanceCoreTests
{
    static int failed, passed;
    static string GamePath,SourceRoot;
    static int Main(string[] args)
    {
        GamePath=args[0];SourceRoot=args[1];
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
            string file=Path.Combine(GamePath,"Star Renegades_Data/Managed/"+new AssemblyName(e.Name).Name+".dll");
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Test("missing manifest refused before hook",()=>Reject(()=>Bootstrap.Read(GamePath,Temporary("missing-manifest")),"manifest"));
        Test("malformed manifest refused before hook",()=>{
            string dir=Temporary("bad-config");
            File.WriteAllText(Path.Combine(dir,"rebalance.json"),"{broken");
            Reject(()=>Bootstrap.Read(GamePath,dir),"manifest");
        });
        Test("fingerprint mismatch refused before hook",()=>{
            string dir=Temporary("bad-fingerprint");
            var c=Config();c["fingerprints"]=new JObject{{"data.unity3d","wrong"},{"Assembly-CSharp.dll","wrong"}};
            File.WriteAllText(Path.Combine(dir,"rebalance.json"),c.ToString());
            Reject(()=>Bootstrap.Read(GamePath,dir),"fingerprint");
        });
        Test("truncated approved manifest refused before hook",()=>{
            string dir=Temporary("truncated-manifest");
            var c=JObject.Parse(File.ReadAllText(Path.Combine(SourceRoot,"rebalance.json")));
            ((JArray)c["additions"]).RemoveAt(0);
            File.WriteAllText(Path.Combine(dir,"rebalance.json"),c.ToString());
            Reject(()=>Bootstrap.Read(GamePath,dir),"manifest");
        });
        Test("approved manifest and game fingerprints accepted",()=>{
            var c=Bootstrap.Read(GamePath,SourceRoot);
            Check((string)c["version"]=="0.3.1","wrong approved version");
        });
        Test("duplicate JSON property refused",()=>{
            string dir=Temporary("duplicate-config");
            File.WriteAllText(Path.Combine(dir,"rebalance.json"),"{\"version\":\"0.3.1\",\"version\":\"0.3.1\"}");
            Reject(()=>Bootstrap.Read(GamePath,dir),"duplicate");
        });
        Test("staging preserves baseline and derived values",()=>{
            var d=Data(); var p=d.PowerData["p"];p.energyActivationTypeValue=(PowerData.EnergyToggleType)1;
            var staged=Overlay.Stage(d,Config(Change("PowerData","p","execution",50,30)));
            Check(d.PowerData["p"].execution==50,"baseline changed during stage");
            staged.Commit(d);
            Check(d.PowerData["p"].execution==30,"execution not applied");
            Check(!Object.ReferenceEquals(p,d.PowerData["p"]),"record not cloned");
            Check(d.PowerData["p"].energyActivationTypeValue==(PowerData.EnergyToggleType)1,"derived enum lost");
            Check(Object.ReferenceEquals(p.effects,d.PowerData["p"].effects),"untouched arrays replaced");
        });
        Test("bad final entry leaves all tables unchanged",()=>{
            var d=Data();var old=d.PowerData;
            Reject(()=>Overlay.Stage(d,Config(Change("PowerData","p","execution",50,30),Change("TraitData","t","hidden",false,true))),"before");
            Check(Object.ReferenceEquals(old,d.PowerData)&&d.PowerData["p"].execution==50,"partial mutation");
        });
        Test("duplicate field rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("PowerData","p","execution",50,30),Change("PowerData","p","execution",50,20))),"duplicate"));
        Test("unknown field rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("PowerData","p","notAField",0,1))),"field"));
        Test("unknown table rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("UnitData","x","health",0,1))),"table"));
        Test("string is not numeric",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("PowerData","p","execution",50,"30"))),"type"));
        Test("fractional integer rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("PowerData","p","execution",50,30.5))),"integer"));
        Test("new modifier uses complete clone",()=>{
            var d=Data();var c=Config();
            ((JArray)c["additions"]).Add(JObject.Parse("{table:'PowerModifierData',id:'hValGue',template:'hMarVanTraits',fields:{id:'hValGue',name:'',description:'',tags:['attack'],modExecution:-5}}"));
            var s=Overlay.Stage(d,c);s.Commit(d);
            var m=d.PowerModifierData["hValGue"];
            Check(m.modExecution==-5 && m.tags[0]=="attack" && m.addEffects.Length==0,"incomplete modifier");
            Check(d.PowerModifierData["hMarVanTraits"].modExecution==0,"template changed");
            Check(!Object.ReferenceEquals(m.addEffects,d.PowerModifierData["hMarVanTraits"].addEffects),"new array shared");
        });
        Test("unapproved new modifier rejected",()=>{
            var c=Config();((JArray)c["additions"]).Add(JObject.Parse("{table:'PowerModifierData',id:'other',template:'hMarVanTraits',fields:{id:'other'}}"));
            Reject(()=>Overlay.Stage(Data(),c),"addition");
        });
        Test("presentation cloned and visible with resolving text",()=>{
            var d=Data();var c=Config(Change("TraitData","t","hidden",true,false),Change("TraitData","t","name","","srrebalance.trait.name"),Change("TraitData","t","description","","srrebalance.trait.desc"));
            foreach(var pair in new[]{new[]{"srrebalance.trait.name","Nimble Fighter"},new[]{"srrebalance.trait.desc","Attacks execute 5 seconds faster."}})
                ((JArray)c["additions"]).Add(new JObject{{"table","TraitStringData"},{"id",pair[0]},{"fields",new JObject{{"id",pair[0]},{"EN",pair[1]}}}});
            var s=Overlay.Stage(d,c);s.Commit(d);
            Check(!d.TraitData["t"].hidden,"trait stays hidden");
            Check(d.TraitStringData.ContainsKey(d.TraitData["t"].description),"unresolved text");
            Check(String.IsNullOrEmpty(d.TraitStringData[d.TraitData["t"].description].FR),"wrong foreign template");
        });
        Test("missing description reference rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("PowerData","p","description","","srrebalance.missing"))),"reference"));
        Test("repeat application refuses before mismatch",()=>{
            var d=Data();var c=Config(Change("PowerData","p","execution",50,30));Overlay.Stage(d,c).Commit(d);
            Reject(()=>Overlay.Stage(d,c),"before");Check(d.PowerData["p"].execution==30,"double applied");
        });
        Test("equipment repair staged and original array preserved",()=>{
            var d=Data();var old=d.EquipmentData["e"].traits;
            var s=Overlay.Stage(d,Config(Change("EquipmentData","e","traits",new[]{"missing"},new[]{"t"})));s.Commit(d);
            Check(old[0]=="missing"&&d.EquipmentData["e"].traits[0]=="t","equipment clone mutated baseline");
            Check(d.EquipmentData["e"].quality=="rare"&&d.EquipmentData["e"].requiredLevel==5,"equipment progression changed");
        });
        Test("missing equipment trait rejected before any commit",()=>{
            var d=Data();var original=d.PowerData;
            Reject(()=>Overlay.Stage(d,Config(Change("PowerData","p","execution",50,30),Change("EquipmentData","e","traits",new[]{"missing"},new[]{"absent"}))),"reference");
            Check(Object.ReferenceEquals(original,d.PowerData)&&d.PowerData["p"].execution==50,"partial mutation on missing reference");
        });
        Test("nonempty equipment roll pool accepted",()=>{
            var d=Data();var s=Overlay.Stage(d,Config(Change("EquipmentData","e","randomTraitsByTagGroups",new[]{"missing"},new[]{"valid"})));s.Commit(d);
            Check(d.EquipmentData["e"].randomTraitsByTagGroups[0]=="valid","roll pool not applied");
        });
        Test("absent equipment roll pool rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("EquipmentData","e","randomTraitsByTagGroups",new[]{"missing"},new[]{"absent"}))),"reference"));
        Test("empty equipment roll pool rejected",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("EquipmentData","e","randomTraitsByTagGroups",new[]{"missing"},new[]{"empty"}))),"empty"));
        Test("camping cost and English text staged together",()=>{
            var d=Data();var s=Overlay.Stage(d,Config(Change("CamaraderieCardData","c","cost",2,1),Change("CamaraderieStringData","aegCard4Desc","EN","Shield","Health")));s.Commit(d);
            Check(d.CamaraderieCardData["c"].cost==1&&d.CamaraderieStringData["aegCard4Desc"].EN=="Health","camping edit missing");
        });
        Test("free camping cost refused for selected cards",()=>Reject(()=>Overlay.Stage(Data(),Config(Change("CamaraderieCardData","c","cost",2,0))),"cost"));
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static string Temporary(string name){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();}catch(InvalidDataException e){Check(e.Message.ToLowerInvariant().Contains(message),"wrong refusal: "+e.Message);return;}throw new Exception("expected refusal: "+message);}
    static JObject Change(string t,string id,string f,object b,object a){return new JObject{{"table",t},{"id",id},{"field",f},{"before",JToken.FromObject(b)},{"after",JToken.FromObject(a)}};}
    static JObject Config(params JObject[] edits){return new JObject{{"version","0.3.1"},{"changes",new JArray(edits)},{"additions",new JArray()}};}
    static GameData Data()
    {
        var d=new GameData();
        Set(d,"PowerData",new Dictionary<string,PowerData>{{"p",new PowerData{id="p",execution=50,description="",effects=new string[0]}}});
        Set(d,"UnitClassData",new Dictionary<string,UnitClassData>());
        Set(d,"UnitModifierData",new Dictionary<string,UnitModifierData>());
        Set(d,"PowerModifierData",new Dictionary<string,PowerModifierData>{{"hMarVanTraits",new PowerModifierData{id="hMarVanTraits",name="",description="",tags=new string[0],addEffects=new string[0],hidden=true}}});
        Set(d,"TraitData",new Dictionary<string,TraitData>{{"t",new TraitData{id="t",name="",description="",hidden=true}}});
        Set(d,"TraitStringData",new Dictionary<string,LocalizedStringData>());
        Set(d,"PowerStringData",new Dictionary<string,LocalizedStringData>());
        d.TraitData["t"].equipmentGenerationTags=new[]{"eqp","TA"};
        Set(d,"EquipmentData",new Dictionary<string,EquipmentData>{{"e",new EquipmentData{id="e",quality="rare",requiredLevel=5,traits=new[]{"missing"},randomTraitsByTagGroups=new[]{"missing"}}}});
        Set(d,"CamaraderieCardData",new Dictionary<string,CamaraderieCardData>{{"c",new CamaraderieCardData{id="c",cost=2}}});
        Set(d,"CamaraderieStringData",new Dictionary<string,LocalizedStringData>{{"aegCard4Desc",new LocalizedStringData{id="aegCard4Desc",EN="Shield"}}});
        Set(d,"TagGroupData",new Dictionary<string,TagGroupData>{{"valid",new TagGroupData{id="valid",tags=new[]{"eqp","TA"}}},{"empty",new TagGroupData{id="empty",tags=new[]{"unknown"}}}});
        return d;
    }
    static void Set(GameData d,string field,object value){typeof(GameData).GetProperty(field).GetSetMethod(true).Invoke(d,new[]{value});}
}
