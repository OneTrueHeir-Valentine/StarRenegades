using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;

namespace SRBugfix
{
    public static class ResourceForecastFix
    {
        public static bool Enabled=false;
        static readonly ConditionalWeakTable<Combat.CombatUnit.EstimatedState,TemporaryArmor> temporaryArmor=new ConditionalWeakTable<Combat.CombatUnit.EstimatedState,TemporaryArmor>();
        static bool subscribed;
        sealed class ArmorStack
        {
            internal UnitEffect Effect;
            internal float Remaining;
            internal Combat.CombatUnitEffect Live;
        }
        sealed class TemporaryArmor
        {
            internal readonly List<ArmorStack> Stacks=new List<ArmorStack>();
            internal TemporaryArmor(Combat.CombatUnit.EstimatedState state)
            {
                foreach(var effect in state.Unit.Unit.ArmorGainingEffects)
                    if(state.Unit.Effects.Has(effect.ID))
                    {
                        var live=state.Unit.Effects[effect.ID];
                        Stacks.Add(new ArmorStack{Effect=effect,Remaining=live.CurrentArmor,Live=live});
                    }
            }
        }
        static TemporaryArmor Get(Combat.CombatUnit.EstimatedState state){return temporaryArmor.GetValue(state,s=>new TemporaryArmor(s));}
        static MethodInfo DamageTarget(){return ReflectionPreviewFix.ResolveTarget();}
        static ConstructorInfo StateConstructor(){return typeof(Combat.CombatUnit.EstimatedState).GetConstructor(new[]{typeof(Combat.CombatUnit)});}
        static MethodInfo PowerTarget(){return typeof(Combat.CombatUnit.EstimatedState).GetMethod("EstimatePower");}
        static void Fingerprint(MethodBase method,int length,string expected)
        {
            if(method==null||method.GetMethodBody()==null)throw new InvalidDataException("Supported resource forecast method was not found.");
            var bytes=method.GetMethodBody().GetILAsByteArray();string actual;
            using(var hash=SHA256.Create())actual=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(bytes.Length!=length||actual!=expected)throw new InvalidDataException("Unsupported resource forecast method fingerprint: "+method.Name);
        }
        public static void ValidateTargets()
        {
            Fingerprint(DamageTarget(),2897,"e387e6bed7f6846aa7728b8de2a669cbfd3656a330bef3e2e727e4e7a4536236");
            Fingerprint(PowerTarget(),1073,"1bcaee986bbea2104003016298eecfcc38f9309bb5f4a91294fbe4f64e3f4f2c");
            Fingerprint(StateConstructor(),375,"51e021af2278cbef650147b16f2b59beb3252e8313de1498adbb428d77a09f6c");
            Fingerprint(typeof(Combat.ExecutePowerEvent).GetMethod("CalculateDamageAgainstTarget",BindingFlags.Instance|BindingFlags.NonPublic),3157,"3e1609de19b05be70429f3c3e2cda3fa22d0cb41adaaa5fcc61679e88d739134");
            Fingerprint(typeof(Combat.CombatUnitEffect).GetMethod("OnEffectArmorDamageApplied",BindingFlags.Instance|BindingFlags.NonPublic),133,"94c68a3b996168ce146351b880b4503fab288d9e9289ab701ea41fc702d07c18");
            TranspileDamage(PatchProcessor.GetOriginalInstructions(DamageTarget())).ToArray();
            TranspilePower(PatchProcessor.GetOriginalInstructions(PowerTarget())).ToArray();
        }
        public static void Install(Harmony harmony)
        {
            ValidateTargets();
            if(!subscribed)
            {
                ProjectedEffectFix.ProjectedEffectAdded+=Added;
                ProjectedEffectFix.ProjectedEffectRemoved+=Removed;
                subscribed=true;
            }
            harmony.Patch(StateConstructor(),postfix:Hook("Initialize"));
            // ProjectedEffectFix locates the original armor setter before this replaces it.
            var damage=Hook("TranspileDamage");damage.priority=Priority.Last;
            harmony.Patch(DamageTarget(),transpiler:damage);
            harmony.Patch(PowerTarget(),transpiler:Hook("TranspilePower"));
        }
        static HarmonyMethod Hook(string name){return new HarmonyMethod(typeof(ResourceForecastFix).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic));}
        static void Initialize(Combat.CombatUnit.EstimatedState __instance){if(Enabled)Get(__instance);}
        static void Added(Combat.CombatUnit.EstimatedState state,UnitEffect effect)
        {
            if(!Enabled||effect.Effect.EffectData.armorGainAbsolute<=0)return;
            var p=Get(state);
            float available=Math.Max(0,state.armorMax.Value-state.armor-p.Stacks.Sum(s=>s.Remaining));
            p.Stacks.Add(new ArmorStack{Effect=effect,Remaining=Math.Min(effect.Effect.EffectData.armorGainAbsolute,available)});
        }
        static void Removed(Combat.CombatUnit.EstimatedState state,UnitEffect effect)
        {
            if(Enabled)Get(state).Stacks.RemoveAll(s=>s.Effect==effect);
        }
        public static float ArmorGainForEstimate(EffectData data){return Enabled?0:data.armorGainAbsolute;}
        public static float ArmorAvailableForDamage(Combat.CombatUnit.EstimatedState state)
        {
            return Enabled?Math.Max(0,state.armor+Get(state).Stacks.Sum(s=>s.Remaining)):state.armor;
        }
        public static float ArmorAbsorptionForEstimate(Combat.CombatUnit.EstimatedState state)
        {
            if(!Enabled||Get(state).Stacks.Count==0)return state.armor;
            return Math.Max(0,Math.Min(ArmorAvailableForDamage(state)-state.armorWeaken.Value,state.armorMax.Value));
        }
        static int Compare(ArmorStack left,ArmorStack right)
        {
            bool a=left.Live!=null&&left.Live.HasExpiry,b=right.Live!=null&&right.Live.HasExpiry;
            if(a&&b)return left.Live.ExpiresOn.CompareTo(right.Live.ExpiresOn);
            if(a!=b)return a?-1:1;
            return left.Effect.ID.CompareTo(right.Effect.ID);
        }
        public static void SetArmorAfterDamage(Combat.CombatUnit.EstimatedState state,float value)
        {
            if(Enabled)
            {
                float requested=Math.Max(0,state.armor-value);var p=Get(state);
                if(p.Stacks.Count>0)
                {
                    // Native stackable armor callbacks debit the first matching effect identity.
                    // Preserve signed stacks and that ordering, including already depleted stacks.
                    foreach(var stack in p.Stacks.OrderBy(s=>s,Comparer<ArmorStack>.Create(Compare)).ToArray())
                    {
                        float consumed=Math.Min(requested,stack.Remaining);
                        var receiver=stack.Effect.Effect.EffectData.isStackable?p.Stacks.First(s=>s.Effect.Effect==stack.Effect.Effect):stack;
                        receiver.Remaining-=consumed;requested-=consumed;
                        if(requested<=0)break;
                    }
                    value=state.armor-Math.Min(Math.Max(0,requested),state.armor);
                }
            }
            state.armor=value;
        }
        public static void SetHealthAfterHealing(Combat.CombatUnit.EstimatedState state,float value)
        {
            // Damage and transfers keep their native setter behavior. Only positive healing is capped here.
            state.health=Enabled&&value>state.health?Math.Min(value,state.Unit.Unit.MaximumHealth):value;
        }
        internal static IEnumerable<CodeInstruction> TranspileDamage(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
            var state=typeof(Combat.CombatUnit.EstimatedState);
            var heal=typeof(UnitPower).GetProperty("Heal").GetGetMethod();var setHealth=state.GetProperty("health").GetSetMethod();
            var getArmor=state.GetProperty("armor").GetGetMethod();var setArmor=state.GetProperty("armor").GetSetMethod();
            var heals=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(heal)).ToArray();
            var reads=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(getArmor)).ToArray();
            var writes=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(setArmor)).ToArray();
            if(heals.Length!=1||reads.Length!=3||writes.Length!=1)throw new InvalidDataException("Unsupported resource forecast damage call layout.");
            int h=heals[0],w=writes[0];
            if(h+2>=code.Count||code[h+1].opcode!=OpCodes.Add||!code[h+2].Calls(setHealth)||
                reads[0]>=reads[1]||reads[1]!=w-3||reads[2]!=w+2||code[w-2].opcode!=OpCodes.Ldloc_S||code[w-1].opcode!=OpCodes.Sub||
                !FloatMathCall(code[reads[0]+1],"Clamp",3)||code[reads[2]+1].opcode!=OpCodes.Sub)
                throw new InvalidDataException("Unsupported resource forecast damage arithmetic layout.");
            Replace(code[h+2],"SetHealthAfterHealing");Replace(code[reads[0]],"ArmorAvailableForDamage");Replace(code[w],"SetArmorAfterDamage");Replace(code[reads[2]],"ArmorAbsorptionForEstimate");
            return code;
        }
        internal static IEnumerable<CodeInstruction> TranspilePower(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();var field=typeof(EffectData).GetField("armorGainAbsolute");
            var sites=Enumerable.Range(0,code.Count).Where(i=>code[i].opcode==OpCodes.Ldfld&&Equals(code[i].operand,field)).ToArray();
            if(sites.Length!=1)throw new InvalidDataException("Unsupported temporary armor forecast grant layout.");
            int at=sites[0];
            if(at<1||!code[at-1].Calls(typeof(Effect).GetProperty("EffectData").GetGetMethod())||at+4>=code.Count||code[at+1].opcode!=OpCodes.Ldc_R4||Convert.ToSingle(code[at+1].operand)!=0||!FloatMathCall(code[at+2],"ClampMin",2)||code[at+3].opcode!=OpCodes.Add)
                throw new InvalidDataException("Unsupported temporary armor forecast grant arithmetic layout.");
            Replace(code[at],"ArmorGainForEstimate");return code;
        }
        static bool FloatMathCall(CodeInstruction instruction,string name,int count)
        {
            var method=instruction.operand as MethodInfo;
            return instruction.opcode==OpCodes.Call&&method!=null&&method.DeclaringType==typeof(Mdi.SRRW.MathExtensions)&&method.Name==name&&method.ReturnType==typeof(float)&&method.GetParameters().Length==count&&method.GetParameters().All(p=>p.ParameterType==typeof(float));
        }
        static void Replace(CodeInstruction instruction,string helper){instruction.opcode=OpCodes.Call;instruction.operand=typeof(ResourceForecastFix).GetMethod(helper);}
    }
}
