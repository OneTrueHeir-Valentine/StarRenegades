using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mdi.SRRW.Engine;

namespace SRBugfix
{
    public static class EnemyAiFix
    {
        public static bool Enabled=false;

        public static MethodInfo ResolveTarget()
        {
            var method=typeof(Combat.CombatAI).GetMethod("SelectAction",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly,
                null,new[]{typeof(string[]),typeof(string).MakeByRefType()},null);
            if(method==null||!method.IsPrivate||method.IsStatic||method.ReturnType!=typeof(bool)||!method.GetParameters()[1].IsOut||method.GetMethodBody()==null)
                throw new InvalidDataException("Unsupported enemy AI SelectAction signature.");
            return method;
        }

        public static void ValidateTargets()
        {
            var instructions=PatchProcessor.GetOriginalInstructions(ResolveTarget()).ToList();
            var addRange=typeof(List<string>).GetMethod("AddRange",new[]{typeof(IEnumerable<string>)});
            var removeAll=typeof(List<string>).GetMethod("RemoveAll",new[]{typeof(Predicate<string>)});
            var tryParse=typeof(int).GetMethod("TryParse",new[]{typeof(string),typeof(int).MakeByRefType()});
            var range=typeof(Mdi.SRRW.Engine.Random).GetMethod("Range",new[]{typeof(int),typeof(int)});
            int add=UniqueCall(instructions,addRange),remove=UniqueCall(instructions,removeAll),parse=UniqueCall(instructions,tryParse),random=UniqueCall(instructions,range);
            int uniform=instructions.Count(i=>{
                var method=i.operand as MethodInfo;
                return IsCall(i)&&method!=null&&method.DeclaringType==typeof(Mdi.SRRW.CollectionExtensions)&&method.Name=="RandomElement"&&
                    method.IsGenericMethod&&method.GetGenericArguments().SequenceEqual(new[]{typeof(string)})&&
                    method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(IList<string>);
            });
            // Validate the predicate actually performs the game action lookup,
            // and that it filters before parsing weights in this supported build.
            var predicates=instructions.Take(remove).Where(i=>i.opcode==OpCodes.Ldftn).Select(i=>i.operand as MethodInfo).ToArray();
            var hasAction=typeof(SRRWGame).GetMethod("HasUnitCombatAction",new[]{typeof(string)});
            bool filter=predicates.Length==1&&predicates[0]!=null&&
                PatchProcessor.GetOriginalInstructions(predicates[0]).Count(i=>IsCall(i)&&Equals(i.operand,hasAction))==1;
            var clamp=instructions.Select((instruction,index)=>new{instruction,index}).Where(item=>{
                var method=item.instruction.operand as MethodInfo;
                return IsCall(item.instruction)&&method!=null&&method.Name=="ClampMin"&&method.ReturnType==typeof(int)&&
                    method.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(int),typeof(int)});
            }).ToArray();
            bool minimum=clamp.Length==1&&clamp[0].index>0&&clamp[0].index<random&&
                instructions[clamp[0].index-1].opcode==OpCodes.Ldc_I4_S&&Convert.ToInt32(instructions[clamp[0].index-1].operand)==100;
            foreach(string name in new[]{"selectableActions","candidateActions","candidateProbabilities"}){
                var field=typeof(Combat.CombatAI).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
                Type expected=name=="candidateProbabilities"?typeof(List<int>):typeof(List<string>);
                if(field==null||field.FieldType!=expected||!instructions.Any(i=>i.opcode==OpCodes.Ldfld&&Equals(i.operand,field)))
                    throw new InvalidDataException("Unsupported enemy AI candidate buffer: "+name);
            }
            if(!(add<remove&&remove<parse&&parse<random)||uniform!=2||!filter||!minimum)
                throw new InvalidDataException("Unsupported enemy AI selection IL shape.");
        }

        static bool IsCall(CodeInstruction instruction){return instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt;}
        static int UniqueCall(List<CodeInstruction> instructions,MethodInfo method)
        {
            var indices=instructions.Select((instruction,index)=>new{instruction,index}).Where(item=>IsCall(item.instruction)&&Equals(item.instruction.operand,method)).ToArray();
            if(method==null||indices.Length!=1)throw new InvalidDataException("Unsupported enemy AI call: "+(method==null?"missing method":method.Name));
            return indices[0].index;
        }

        public static void Install(Harmony harmony)
        {
            ValidateTargets();
            harmony.Patch(ResolveTarget(),prefix:new HarmonyMethod(typeof(EnemyAiFix).GetMethod("SelectActionPrefix",BindingFlags.Static|BindingFlags.NonPublic)));
        }

        static bool SelectActionPrefix(Combat.CombatAI __instance,string[] __0,ref string __1,ref bool __result)
        {
            if(!Enabled||__0==null||__0.Length==0||(__0.Length&1)!=0)return true;
            int sum=0;
            for(int i=0;i<__0.Length;i+=2){
                int weight;
                if(!int.TryParse(__0[i],out weight)||weight<0||weight>int.MaxValue-sum||
                    string.IsNullOrEmpty(__0[i+1])||!__instance.Game.HasUnitCombatAction(__0[i+1]))return true;
                sum+=weight;
            }
            // Keep the original percentage gap when weights total less than 100.
            // Validate every pair before consuming RNG or replacing native behavior.
            int roll=Mdi.SRRW.Engine.Random.Range(0,Math.Max(100,sum));
            for(int i=0;i<__0.Length;i+=2){
                int weight=int.Parse(__0[i]);
                if(roll<weight){__1=__0[i+1];__result=true;return false;}
                roll-=weight;
            }
            __1=null;__result=false;return false;
        }
    }
}
