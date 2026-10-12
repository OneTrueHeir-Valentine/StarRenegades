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
        Test("AppliesExactly346FieldsAnd36Pools",()=>{
            var manifest=Manifest();var data=Fixture(manifest);var powers=data.PowerData;
            var staged=Overlay.Stage(data,manifest);Equal(staged.ChangedFields,346);Equal(staged.AddedRecords,36);staged.Commit(data);
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
            var m=Manifest();var d=Fixture(m);var old=d.EquipmentData;((JObject)((JArray)m["changes"]).Last)["before"]=new JArray("wrong");
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
            var m=Manifest();var d=Fixture(m);Overlay.Stage(d,m).Commit(d);Reject(()=>Overlay.Stage(d,m),"mismatch");
        });
        Test("RejectsDuplicateJsonProperty",()=>{
            string dir=Temporary("duplicate");File.WriteAllText(Path.Combine(dir,"fixes.json"),"{\"version\":\"0.1.0\",\"version\":\"0.1.0\"}");Reject(()=>Bootstrap.Read(gameRoot,dir),"duplicate");
        });
        Test("RejectsModifiedManifest",()=>{
            string dir=Temporary("modified");var m=Manifest();m["changes"][336]["after"]=5;File.WriteAllText(Path.Combine(dir,"fixes.json"),m.ToString());Reject(()=>Bootstrap.Read(gameRoot,dir),"checksum");
        });
        Test("RejectsMissingManifest",()=>Reject(()=>Bootstrap.Read(gameRoot,Temporary("missing")),"missing"));
        Test("AcceptsPinnedManifestAndGame",()=>Equal((string)Bootstrap.Read(gameRoot,sourceRoot)["version"],"0.1.0"));
        Test("AddsMirroredPoolWithFreshTagsHash",()=>{
            var m=CoreManifest();var d=Fixture(m);Set(d,"TagGroupData",new Dictionary<string,TagGroupData>());
            var template=new TagGroupData{id="tagSwordElementalA3",tags=new[]{"eqp","sword","lvl3"}};((UnityEngine.ISerializationCallbackReceiver)template).OnAfterDeserialize();
            d.TagGroupData.Add(template.id,template);d.TraitData.Add("candidate",new TraitData{id="candidate",equipmentGenerationTags=new[]{"eqp","sword","kinetic","lvl3"}});
            m["dictionary_counts"]["TagGroupData"]=1;m["dictionary_counts"]["TraitData"]=d.TraitData.Count;
            ((JArray)m["additions"]).Add(new JObject{{"table","TagGroupData"},{"id","tagKineticElementalA3"},{"template",template.id},{"fields",new JObject{{"id","tagKineticElementalA3"},{"tags",new JArray("eqp","kinetic","lvl3")}}}});
            var staged=Overlay.Stage(d,m);staged.Commit(d);var group=d.TagGroupData["tagKineticElementalA3"];
            Check(group.tagsHash.SetEquals(new[]{"eqp","kinetic","lvl3"}),"new tags hash is stale");Check(template.tagsHash.Contains("sword")&&!template.tagsHash.Contains("kinetic"),"template mutated");
        });
        Test("CorrectsBlasterTagWithoutChangingOtherFields",()=>{
            var m=CoreManifest();var d=Fixture(m);Set(d,"TagGroupData",new Dictionary<string,TagGroupData>());
            var trait=new TraitData{id="eqtRemoveBuffsLight",equipmentGenerationTags=new[]{"eqp","sword","blasteer","kinetic","rifle","generalb"},powerModifiers=new[]{"unchanged"}};d.TraitData.Add(trait.id,trait);
            m["dictionary_counts"]["TagGroupData"]=0;m["dictionary_counts"]["TraitData"]=d.TraitData.Count;
            ((JArray)m["changes"]).Add(new JObject{{"table","TraitData"},{"id",trait.id},{"field","equipmentGenerationTags"},{"before",new JArray(trait.equipmentGenerationTags)},{"after",new JArray("eqp","sword","blaster","kinetic","rifle","generalb")}});
            Overlay.Stage(d,m).Commit(d);Check(d.TraitData[trait.id].equipmentGenerationTags.Contains("blaster"),"corrected tag missing");Check(trait.equipmentGenerationTags.Contains("blasteer"),"baseline trait mutated");Check(Object.ReferenceEquals(trait.powerModifiers,d.TraitData[trait.id].powerModifiers),"effect modifiers changed");
        });
        Test("BadFinalPoolLeavesEveryTableUnchanged",()=>{
            var m=Manifest();var d=Fixture(m);var equipment=d.EquipmentData;var traits=d.TraitData;var groups=d.TagGroupData;
            ((JObject)((JArray)m["additions"]).Last)["fields"]["tags"]=new JArray("eqp","wrong-slot","lvl10");
            Reject(()=>Overlay.Stage(d,m),"counterpart");Check(Object.ReferenceEquals(equipment,d.EquipmentData)&&Object.ReferenceEquals(traits,d.TraitData)&&Object.ReferenceEquals(groups,d.TagGroupData),"partial mutation");
        });
        Test("EmptyMirroredPoolRejected",()=>{
            var m=Manifest();var d=Fixture(m);foreach(var t in d.TraitData.Values)t.equipmentGenerationTags=new string[0];
            // Keep the separately reviewed tag corrections' before-values valid.
            foreach(var e in ((JArray)m["changes"]).Where(e=>(string)e["table"]=="TraitData"))d.TraitData[(string)e["id"]].equipmentGenerationTags=((JArray)e["before"]).Select(t=>(string)t).ToArray();
            Reject(()=>Overlay.Stage(d,m),"empty");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static JObject Manifest(){return JObject.Parse(File.ReadAllText(Path.Combine(sourceRoot,"fixes.json")));}
    static JObject CoreManifest(){var m=Manifest();m["changes"]=new JArray(((JArray)m["changes"]).Where(e=>(string)e["table"]!="TraitData").Select(e=>e.DeepClone()));m["additions"]=new JArray();return m;}
    static string Temporary(string name){string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"core-fixtures/"+name);Directory.CreateDirectory(p);return p;}
    static GameData Fixture(JObject manifest)
    {
        var d=(GameData)FormatterServices.GetUninitializedObject(typeof(GameData));
        Set(d,"EquipmentData",new Dictionary<string,EquipmentData>());Set(d,"PowerModifierData",new Dictionary<string,PowerModifierData>());Set(d,"CamaraderieStringData",new Dictionary<string,LocalizedStringData>());Set(d,"TraitData",new Dictionary<string,TraitData>());Set(d,"TagGroupData",new Dictionary<string,TagGroupData>());Set(d,"PowerData",new Dictionary<string,PowerData>());
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
        foreach(var a in (JArray)manifest["additions"])
        {
            string template=(string)a["template"];string[] tags=((JArray)a["fields"]["tags"]).Select(t=>(string)t).ToArray();
            var group=new TagGroupData{id=template,tags=tags.Select(t=>t=="kinetic"||t=="blaster"||t=="rifle"?"sword":t).ToArray()};((UnityEngine.ISerializationCallbackReceiver)group).OnAfterDeserialize();d.TagGroupData[template]=group;
            string level=tags.First(t=>t.StartsWith("lvl",StringComparison.Ordinal));d.TraitData["fixture-"+level]=new TraitData{id="fixture-"+level,equipmentGenerationTags=new[]{"eqp","sword","kinetic","blaster","rifle",level}};
        }
        manifest["dictionary_counts"]=new JObject{{"EquipmentData",d.EquipmentData.Count},{"PowerModifierData",d.PowerModifierData.Count},{"CamaraderieStringData",d.CamaraderieStringData.Count},{"TraitData",d.TraitData.Count},{"TagGroupData",d.TagGroupData.Count}};
        return d;
    }
    static IDictionary Table(GameData d,string name){return (IDictionary)typeof(GameData).GetProperty(name).GetValue(d,null);}
    static void Set(GameData d,string name,object value){typeof(GameData).GetProperty(name).GetSetMethod(true).Invoke(d,new[]{value});}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Equal(object a,object b){Check(Object.Equals(a,b),"expected "+b+", got "+a);}
    static void Reject(Action action,string word){try{action();}catch(InvalidDataException e){Check(e.Message.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0,"wrong rejection: "+e.Message);return;}throw new Exception("expected rejection: "+word);}
}
