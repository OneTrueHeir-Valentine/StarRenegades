using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
using SRBugfix;

internal static class CoreTests
{
    static int passed,failed;
    static string gameRoot,sourceRoot;
    static int Main(string[] args)
    {
        gameRoot=args[0];sourceRoot=args[1];
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string file=Path.Combine(gameRoot,"Star Renegades_Data/Managed/"+new AssemblyName(e.Name).Name+".dll");
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Test("AppliesExactly341CoreFields",()=>{
            var manifest=Manifest();var data=Fixture(manifest);var powers=data.PowerData;
            var staged=Overlay.Stage(data,manifest);Equal(staged.ChangedFields,341);staged.Commit(data);
            foreach(var e in (JArray)manifest["changes"]){var row=Table(data,(string)e["table"])[(string)e["id"]];var field=row.GetType().GetField((string)e["field"]);Check(JToken.DeepEquals(JToken.FromObject(field.GetValue(row)),e["after"]),"wrong after value");}
            Check(Object.ReferenceEquals(powers,data.PowerData),"character powers replaced");
        });
        Test("PreservesRecordsAndUnchangedArrays",()=>{
            var m=Manifest();var d=Fixture(m);var item=d.EquipmentData.Values.First();var groups=item.randomTraitsByTagGroups;
            var old=d.EquipmentData;var oldTraits=item.traits;var staged=Overlay.Stage(d,m);
            Check(Object.ReferenceEquals(old,d.EquipmentData)&&Object.ReferenceEquals(oldTraits,item.traits),"staging mutated original");
            staged.Commit(d);var replacement=d.EquipmentData[item.id];
            Check(!Object.ReferenceEquals(replacement,item),"record not cloned");Check(Object.ReferenceEquals(replacement.randomTraitsByTagGroups,groups),"unchanged arrays replaced");
            Check(replacement.quality=="rare"&&replacement.requiredLevel==5&&replacement.resourceValue==33,"metadata lost");
            Check(!Object.ReferenceEquals(replacement.traits,oldTraits),"changed array shared");
            staged.Restore(d);Check(Object.ReferenceEquals(old,d.EquipmentData),"original dictionary not restored");
        });
        Test("RejectsBadLastEditWithoutMutation",()=>{
            var m=Manifest();var d=Fixture(m);var old=d.EquipmentData;((JObject)((JArray)m["changes"]).Last)["before"]="wrong";
            Reject(()=>Overlay.Stage(d,m),"before");Check(Object.ReferenceEquals(old,d.EquipmentData),"partial mutation");
        });
        Test("RejectsMissingEquipmentTrait",()=>{
            var m=Manifest();var d=Fixture(m);((JArray)m["changes"][0]["after"])[0]="missingTrait";Reject(()=>Overlay.Stage(d,m),"trait");
        });
        Test("RejectsUnexpectedTable",()=>{
            var m=Manifest();var d=Fixture(m);m["changes"][0]["table"]="PowerData";Reject(()=>Overlay.Stage(d,m),"table");
        });
        Test("RejectsUnexpectedField",()=>{
            var m=Manifest();var d=Fixture(m);m["changes"][0]["field"]="resourceValue";Reject(()=>Overlay.Stage(d,m),"field");
        });
        Test("RejectsDuplicateField",()=>{
            var m=Manifest();var d=Fixture(m);((JArray)m["changes"]).Add(m["changes"][0].DeepClone());Reject(()=>Overlay.Stage(d,m),"duplicate");
        });
        Test("RejectsWrongType",()=>{
            var m=Manifest();var d=Fixture(m);m["changes"][336]["after"]="zero";Reject(()=>Overlay.Stage(d,m),"type");
        });
        Test("RejectsAdditions",()=>{
            var m=Manifest();var d=Fixture(m);((JArray)m["additions"]).Add(new JObject{{"table","TraitData"},{"id","invented"}});Reject(()=>Overlay.Stage(d,m),"addition");
        });
        Test("RejectsConcurrentTableReplacement",()=>{
            var m=Manifest();var d=Fixture(m);var staged=Overlay.Stage(d,m);Set(d,"EquipmentData",new Dictionary<string,EquipmentData>());Reject(()=>staged.Commit(d),"changed");
        });
        Test("RejectsRepeatApplication",()=>{
            var m=Manifest();var d=Fixture(m);Overlay.Stage(d,m).Commit(d);Reject(()=>Overlay.Stage(d,m),"before");
        });
        Test("RejectsDuplicateJsonProperty",()=>{
            string dir=Temporary("duplicate");File.WriteAllText(Path.Combine(dir,"fixes.json"),"{\"version\":\"0.1.0\",\"version\":\"0.1.0\"}");Reject(()=>Bootstrap.Read(gameRoot,dir),"duplicate");
        });
        Test("RejectsModifiedManifest",()=>{
            string dir=Temporary("modified");var m=Manifest();m["changes"][336]["after"]=5;File.WriteAllText(Path.Combine(dir,"fixes.json"),m.ToString());Reject(()=>Bootstrap.Read(gameRoot,dir),"checksum");
        });
        Test("RejectsMissingManifest",()=>Reject(()=>Bootstrap.Read(gameRoot,Temporary("missing")),"missing"));
        Test("AcceptsPinnedManifestAndGame",()=>Equal((string)Bootstrap.Read(gameRoot,sourceRoot)["version"],"0.1.0"));
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static JObject Manifest(){return JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot,"fixes.json")));}
    static string Temporary(string name){string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"core-fixtures/"+name);Directory.CreateDirectory(p);return p;}
    static GameData Fixture(JObject manifest)
    {
        var d=(GameData)FormatterServices.GetUninitializedObject(typeof(GameData));
        Set(d,"EquipmentData",new Dictionary<string,EquipmentData>());Set(d,"PowerModifierData",new Dictionary<string,PowerModifierData>());Set(d,"CamaraderieStringData",new Dictionary<string,LocalizedStringData>());Set(d,"TraitData",new Dictionary<string,TraitData>());Set(d,"PowerData",new Dictionary<string,PowerData>());
        foreach(var e in (JArray)manifest["changes"])
        {
            string tn=(string)e["table"],id=(string)e["id"];var table=Table(d,tn);object row;
            if(!table.Contains(id))
            {
                Type type=table.GetType().GetGenericArguments()[1];row=Activator.CreateInstance(type);
                foreach(var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance))if(f.FieldType==typeof(string))f.SetValue(row,"");else if(f.FieldType==typeof(string[]))f.SetValue(row,new string[0]);
                type.GetField("id").SetValue(row,id);table.Add(id,row);
                var eq=row as EquipmentData;if(eq!=null){eq.quality="rare";eq.requiredLevel=5;eq.resourceValue=33;eq.randomTraitsByTagGroups=new[]{"untouchedGroup"};}
            }
            else row=table[id];
            var field=row.GetType().GetField((string)e["field"]);field.SetValue(row,e["before"].ToObject(field.FieldType));
            if(tn=="EquipmentData")foreach(string trait in ((JArray)e["after"]).Select(t=>(string)t))d.TraitData[trait]=new TraitData{id=trait};
        }
        manifest["dictionary_counts"]=new JObject{{"EquipmentData",d.EquipmentData.Count},{"PowerModifierData",d.PowerModifierData.Count},{"CamaraderieStringData",d.CamaraderieStringData.Count}};
        return d;
    }
    static IDictionary Table(GameData d,string name){return (IDictionary)typeof(GameData).GetProperty(name).GetValue(d,null);}
    static void Set(GameData d,string name,object value){typeof(GameData).GetProperty(name).GetSetMethod(true).Invoke(d,new[]{value});}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Equal(object a,object b){Check(Object.Equals(a,b),"expected "+b+", got "+a);}
    static void Reject(Action action,string word){try{action();}catch(InvalidDataException e){Check(e.Message.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0,"wrong rejection: "+e.Message);return;}throw new Exception("expected rejection: "+word);}
}
