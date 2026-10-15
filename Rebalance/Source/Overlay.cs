using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
namespace SRRebalance
{
    public static class Overlay
    {
        static readonly string[] Tables={"PowerData","UnitClassData","UnitModifierData","PowerModifierData","TraitData","TraitStringData","PowerStringData","EquipmentData","CamaraderieCardData","CamaraderieStringData"};
        static readonly MethodInfo CloneMethod=typeof(Object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        public sealed class StageResult
        {
            internal Dictionary<string,IDictionary> Originals, Replacements;
            public int ChangedFields, AddedRecords;
            public string Counts { get {return String.Join(", ",Tables.Select(t=>t+"="+Replacements[t].Count).ToArray());} }
            public void Commit(GameData data)
            {
                // Resolve every setter before the first mutation. The inspected game uses plain setters.
                var setters=Tables.ToDictionary(t=>t,t=>typeof(GameData).GetProperty(t).GetSetMethod(true));
                if(setters.Any(p=>p.Value==null))throw new InvalidDataException("missing table setter");
                if(Tables.Any(t=>!Object.ReferenceEquals(typeof(GameData).GetProperty(t).GetValue(data,null),Originals[t])))
                    throw new InvalidDataException("tables changed since staging");
                try {foreach(var t in Tables)setters[t].Invoke(data,new object[]{Replacements[t]});}
                catch {Restore(data);throw;}
            }
            public void Restore(GameData data){foreach(var t in Tables)typeof(GameData).GetProperty(t).GetSetMethod(true).Invoke(data,new object[]{Originals[t]});}
        }
        public static StageResult Stage(GameData data,JObject manifest)
        {
            if(data==null||manifest==null)throw new InvalidDataException("missing data or manifest");
            if((string)manifest["version"]!="0.3.5")throw new InvalidDataException("unsupported manifest version");
            var changes=manifest["changes"] as JArray;var additions=manifest["additions"] as JArray;
            if(changes==null||additions==null)throw new InvalidDataException("missing changes/additions arrays");
            var result=new StageResult{Originals=new Dictionary<string,IDictionary>(),Replacements=new Dictionary<string,IDictionary>()};
            foreach(var t in Tables)
            {
                var original=typeof(GameData).GetProperty(t).GetValue(data,null) as IDictionary;
                if(original==null)throw new InvalidDataException("missing table "+t);
                var expected=manifest["dictionary_counts"]?[t];
                if(expected!=null&&original.Count!=(int)expected)throw new InvalidDataException("table count mismatch "+t);
                var copy=(IDictionary)Activator.CreateInstance(original.GetType());
                foreach(DictionaryEntry pair in original)copy.Add(pair.Key,pair.Value);
                result.Originals.Add(t,original);result.Replacements.Add(t,copy);
            }
            var seen=new HashSet<string>();var cloned=new HashSet<string>();
            foreach(var token in changes)
            {
                var edit=token as JObject;if(edit==null)throw new InvalidDataException("change is not an object");
                string table=Required(edit,"table"),id=Required(edit,"id"),field=Required(edit,"field");
                var dict=GetTable(result,table);string key=table+"/"+id,fieldKey=key+"/"+field;
                if(!seen.Add(fieldKey))throw new InvalidDataException("duplicate field "+fieldKey);
                if(!dict.Contains(id))throw new InvalidDataException("unknown record "+key);
                if(field=="id")throw new InvalidDataException("existing id field cannot change "+key);
                if(table=="EquipmentData"&&field!="traits"&&field!="randomTraitsByTagGroups")throw new InvalidDataException("unsupported equipment field "+fieldKey);
                if(table=="CamaraderieCardData"&&field!="cost")throw new InvalidDataException("unsupported camping field "+fieldKey);
                if(table=="CamaraderieStringData"&&(id!="aegCard4Desc"||field!="EN"))throw new InvalidDataException("unsupported camping text field "+fieldKey);
                object record=dict[id];var member=GetField(record.GetType(),field);
                object before=Typed(edit["before"],member.FieldType,fieldKey),after=Typed(edit["after"],member.FieldType,fieldKey);
                if(!Equal(member.GetValue(record),before))throw new InvalidDataException("before value mismatch "+fieldKey);
                if(cloned.Add(key)){record=CloneMethod.Invoke(record,null);dict[id]=record;}
                member.SetValue(record,after);result.ChangedFields++;
            }
            foreach(var token in additions)
            {
                var addition=token as JObject;if(addition==null)throw new InvalidDataException("addition is not an object");
                string table=Required(addition,"table"),id=Required(addition,"id");var dict=GetTable(result,table);
                bool modifier=table=="PowerModifierData"&&id=="hValGue"&&(string)addition["template"]=="hMarVanTraits";
                bool text=(table=="TraitStringData"||table=="PowerStringData")&&id.StartsWith("srrebalance.",StringComparison.Ordinal);
                if(!modifier&&!text)throw new InvalidDataException("unapproved addition "+table+"/"+id);
                if(dict.Contains(id))throw new InvalidDataException("duplicate addition "+table+"/"+id);
                object record;
                if(modifier)
                {
                    if(!dict.Contains("hMarVanTraits"))throw new InvalidDataException("missing modifier template");
                    record=CloneMethod.Invoke(dict["hMarVanTraits"],null);
                    foreach(var f in record.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
                        if(f.FieldType.IsArray&&f.GetValue(record)!=null)f.SetValue(record,((Array)f.GetValue(record)).Clone());
                }
                else record=new LocalizedStringData();
                var fields=addition["fields"] as JObject;if(fields==null)throw new InvalidDataException("missing addition fields");
                foreach(var p in fields.Properties())
                {
                    var f=GetField(record.GetType(),p.Name);f.SetValue(record,Typed(p.Value,f.FieldType,table+"/"+id+"/"+p.Name));
                }
                if((string)GetField(record.GetType(),"id").GetValue(record)!=id)throw new InvalidDataException("addition id mismatch");
                if(text&&String.IsNullOrEmpty(((LocalizedStringData)record).EN))throw new InvalidDataException("empty English text");
                dict.Add(id,record);result.AddedRecords++;
            }
            foreach(var token in changes)
            {
                var e=(JObject)token;string table=(string)e["table"],field=(string)e["field"],value=(string)(e["after"] is JValue&&e["after"].Type==JTokenType.String?e["after"]:null);
                string id=(string)e["id"];
                if(table=="EquipmentData")
                {
                    var item=(EquipmentData)result.Replacements[table][id];
                    if(field=="traits")foreach(var trait in item.traits)
                        if(!result.Replacements["TraitData"].Contains(trait))throw new InvalidDataException("equipment trait reference missing "+id+"/"+trait);
                    if(field=="randomTraitsByTagGroups")foreach(var groupID in item.randomTraitsByTagGroups)
                    {
                        TagGroupData group;
                        if(data.TagGroupData==null||!data.TagGroupData.TryGetValue(groupID,out group))throw new InvalidDataException("equipment pool reference missing "+id+"/"+groupID);
                        if(group.tags==null||!result.Replacements["TraitData"].Values.Cast<TraitData>().Any(t=>t.equipmentGenerationTags!=null&&new HashSet<string>(t.equipmentGenerationTags).IsSupersetOf(group.tags)))
                            throw new InvalidDataException("empty equipment pool "+id+"/"+groupID);
                    }
                }
                if(table=="CamaraderieCardData")
                {
                    int cost=((CamaraderieCardData)result.Replacements[table][id]).cost;
                    if(cost<1||cost>5)throw new InvalidDataException("camping cost outside supported budget "+id);
                }
                if(table=="CamaraderieStringData"&&String.IsNullOrEmpty(((LocalizedStringData)result.Replacements[table][id]).EN))throw new InvalidDataException("empty camping English text "+id);
                if((field=="name"||field=="description")&&!String.IsNullOrEmpty(value))
                {
                    string strings=table=="PowerData"?"PowerStringData":table=="TraitData"?"TraitStringData":null;
                    if(strings!=null&&!result.Replacements[strings].Contains(value))throw new InvalidDataException("unresolved text reference "+value);
                }
            }
            return result;
        }
        static IDictionary GetTable(StageResult result,string table)
        {IDictionary value;if(!result.Replacements.TryGetValue(table,out value))throw new InvalidDataException("unknown table "+table);return value;}
        static string Required(JObject o,string key)
        {var t=o[key];if(t==null||t.Type!=JTokenType.String||String.IsNullOrEmpty((string)t))throw new InvalidDataException("missing string "+key);return (string)t;}
        static FieldInfo GetField(Type type,string name)
        {var f=type.GetField(name,BindingFlags.Public|BindingFlags.Instance);if(f==null)throw new InvalidDataException("unknown field "+type.Name+"/"+name);return f;}
        static object Typed(JToken token,Type type,string key)
        {
            if(token==null||token.Type==JTokenType.Null)throw new InvalidDataException("null or missing value "+key);
            if(type==typeof(string)&&token.Type==JTokenType.String)return (string)token;
            if(type==typeof(bool)&&token.Type==JTokenType.Boolean)return (bool)token;
            if(type==typeof(int)&&(token.Type==JTokenType.Integer||token.Type==JTokenType.Float))
            {
                double n=(double)token;if(Double.IsNaN(n)||Double.IsInfinity(n)||n!=Math.Truncate(n)||n<Int32.MinValue||n>Int32.MaxValue)throw new InvalidDataException("invalid integer "+key);return (int)n;
            }
            if(type==typeof(float)&&(token.Type==JTokenType.Integer||token.Type==JTokenType.Float))
            {
                double n=(double)token;if(Double.IsNaN(n)||Double.IsInfinity(n)||n>Single.MaxValue||n< -Single.MaxValue)throw new InvalidDataException("invalid float "+key);return (float)n;
            }
            if(type==typeof(string[])&&token is JArray)
            {
                if(token.Any(t=>t.Type!=JTokenType.String))throw new InvalidDataException("invalid array type "+key);return token.Select(t=>(string)t).ToArray();
            }
            throw new InvalidDataException("invalid type "+key+" expected "+type.Name);
        }
        static bool Equal(object left,object right)
        {
            if(left is string[]&&right is string[])return ((string[])left).SequenceEqual((string[])right);
            return Object.Equals(left,right);
        }
    }
}
