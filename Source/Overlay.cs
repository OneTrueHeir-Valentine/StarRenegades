using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
namespace SRBugfix
{
    public static class Overlay
    {
        static readonly string[] Tables={"EquipmentData","PowerModifierData","CamaraderieStringData"};
        static readonly MethodInfo Clone=typeof(Object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        public sealed class StageResult
        {
            public int ChangedFields;
            internal Dictionary<string,IDictionary> Original,Replacement;
            internal IDictionary Traits;
            public void Commit(GameData data)
            {
                if(Tables.Any(t=>!Object.ReferenceEquals(Table(data,t),Original[t]))||!Object.ReferenceEquals(data.TraitData,Traits))throw new InvalidDataException("data tables changed since staging");
                var setters=Tables.ToDictionary(t=>t,t=>typeof(GameData).GetProperty(t).GetSetMethod(true));
                if(setters.Any(s=>s.Value==null))throw new InvalidDataException("missing table setter");
                try{foreach(string t in Tables)setters[t].Invoke(data,new object[]{Replacement[t]});}
                catch{Restore(data);throw;}
            }
            public void Restore(GameData data){foreach(string t in Tables)typeof(GameData).GetProperty(t).GetSetMethod(true).Invoke(data,new object[]{Original[t]});}
        }
        public static StageResult Stage(GameData data,JObject manifest)
        {
            if(data==null||manifest==null)throw new InvalidDataException("missing data");
            if((string)manifest["version"]!="0.1.0")throw new InvalidDataException("unsupported version");
            var changes=manifest["changes"] as JArray;var additions=manifest["additions"] as JArray;
            if(changes==null||additions==null)throw new InvalidDataException("missing changes or additions");
            if(additions.Count!=0)throw new InvalidDataException("additions are not allowed");
            var result=new StageResult{Original=new Dictionary<string,IDictionary>(),Replacement=new Dictionary<string,IDictionary>(),Traits=data.TraitData};
            if(result.Traits==null)throw new InvalidDataException("missing trait table");
            foreach(string t in Tables)
            {
                IDictionary original=Table(data,t);if(original==null)throw new InvalidDataException("missing table "+t);
                var expected=manifest["dictionary_counts"]?[t];if(expected==null||original.Count!=(int)expected)throw new InvalidDataException("table count mismatch "+t);
                var copy=(IDictionary)Activator.CreateInstance(original.GetType());foreach(DictionaryEntry pair in original)copy.Add(pair.Key,pair.Value);
                result.Original.Add(t,original);result.Replacement.Add(t,copy);
            }
            var fields=new HashSet<string>();var clones=new HashSet<string>();
            foreach(var token in changes)
            {
                var edit=token as JObject;if(edit==null)throw new InvalidDataException("change must be an object");
                string t=Required(edit,"table"),id=Required(edit,"id"),field=Required(edit,"field"),rowKey=t+"/"+id,key=rowKey+"/"+field;
                if(!result.Replacement.ContainsKey(t))throw new InvalidDataException("unsupported table "+t);
                bool allowed=t=="EquipmentData"&&field=="traits"||t=="PowerModifierData"&&(id=="camParTrait1"||id=="camParTrait2")&&(field=="setCriticalTransferShieldAbsolute"||field=="modCriticalTransferShieldAbsolute")||t=="CamaraderieStringData"&&id=="aegCard4Desc"&&field=="EN";
                if(!allowed)throw new InvalidDataException("unsupported field "+key);
                if(!fields.Add(key))throw new InvalidDataException("duplicate field "+key);
                IDictionary rows=result.Replacement[t];if(!rows.Contains(id))throw new InvalidDataException("missing record "+rowKey);
                object record=rows[id];var member=record.GetType().GetField(field);if(member==null)throw new InvalidDataException("missing field "+key);
                object before=Typed(edit["before"],member.FieldType,key),after=Typed(edit["after"],member.FieldType,key);
                if(!Equal(member.GetValue(record),before))throw new InvalidDataException("before value mismatch "+key);
                if(clones.Add(rowKey)){record=Clone.Invoke(record,null);rows[id]=record;}
                member.SetValue(record,after);result.ChangedFields++;
                if(t=="EquipmentData")foreach(string trait in (string[])after)if(!result.Traits.Contains(trait))throw new InvalidDataException("missing equipment trait "+trait);
                if(t=="CamaraderieStringData"&&String.IsNullOrEmpty((string)after))throw new InvalidDataException("empty English description");
            }
            return result;
        }
        static IDictionary Table(GameData data,string name){return (IDictionary)typeof(GameData).GetProperty(name).GetValue(data,null);}
        static string Required(JObject value,string key){var token=value[key];if(token==null||token.Type!=JTokenType.String||String.IsNullOrEmpty((string)token))throw new InvalidDataException("missing "+key);return (string)token;}
        static object Typed(JToken token,Type type,string key)
        {
            if(token==null)throw new InvalidDataException("missing value "+key);
            if(type==typeof(string)&&token.Type==JTokenType.String)return (string)token;
            if(type==typeof(string[])&&token is JArray&&token.All(t=>t.Type==JTokenType.String))return token.Select(t=>(string)t).ToArray();
            if(type==typeof(float)&&(token.Type==JTokenType.Integer||token.Type==JTokenType.Float))
            {double n=(double)token;if(!Double.IsNaN(n)&&!Double.IsInfinity(n)&&n>=-Single.MaxValue&&n<=Single.MaxValue)return (float)n;}
            throw new InvalidDataException("invalid type "+key);
        }
        static bool Equal(object a,object b){return a is string[]&&b is string[]?((string[])a).SequenceEqual((string[])b):Object.Equals(a,b);}
    }
}
