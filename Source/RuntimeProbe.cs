using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Mdi.SRRW.Engine.Camaraderie;
using Newtonsoft.Json.Linq;
namespace SRBugfix
{
    // Developer acceptance only. The flag is never shipped in a player package.
    public static class RuntimeProbe
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static void Check(bool value,string message){if(!value)throw new Exception("Acceptance failed: "+message);}
        static object Clone(object value){return typeof(object).GetMethod("MemberwiseClone",Flags).Invoke(value,null);}
        static void Set(object value,string name,object data){for(Type t=value.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(value,data);return;}}throw new Exception("Missing fixture field "+name);}
        static SRRWGame Context(GameData data)
        {
            var game=(SRRWGame)FormatterServices.GetUninitializedObject(typeof(SRRWGame));
            foreach(var f in typeof(SRRWGame).GetFields(Flags))if(f.FieldType.IsGenericType&&(f.FieldType.GetGenericTypeDefinition()==typeof(Dictionary<,>)||f.FieldType.GetGenericTypeDefinition()==typeof(List<>)||f.FieldType.GetGenericTypeDefinition()==typeof(HashSet<>)))f.SetValue(game,Activator.CreateInstance(f.FieldType));
            Set(game,"gameData",data);Set(game,"currentDifficulty",data.DifficultyData.Values.First());
            foreach(var pair in new[]{new[]{"cachedUnitResourceType","unitResourceType"},new[]{"cachedEquipmentResourceType","equipmentResourceType"},new[]{"cachedSkillResourceType","skillResourceType"},new[]{"cachedMetaPurchasedUnlockResourceType","metaPurchasedUnlockResourceType"},new[]{"cachedMetaProgressionUnlockResourceType","metaProgressionUnlockResourceType"},new[]{"cachedHeroPerkUpgradeResourceType","heroPerkUpgradeResourceType"},new[]{"cachedSquadFocusResourceType","squadFocusResourceType"}})Set(game,pair[0],game.GetResourceType((string)typeof(GameRulesData).GetField(pair[1]).GetValue(data.GameRulesData)));
            return game;
        }
        static Unit Fresh(SRRWGame game,GameData data){var unit=new Unit(game,data.UnitData["hPar"]);unit.Initialize(1,true);return unit;}
        public static void Run(GameData data,JObject manifest)
        {
            var game=Context(data);int fixedItems=0,rolls=0,labels=0;
            var generatedField=typeof(Equipment).GetField("generatedTraits",Flags);
            var added=new HashSet<string>(((JArray)manifest["additions"]).Select(a=>(string)a["id"]));
            foreach(var edit in ((JArray)manifest["changes"]).Where(e=>(string)e["table"]=="EquipmentData"))
            {
                var ed=data.EquipmentData[(string)edit["id"]];var eq=new Equipment(game,ed);
                Check(ed.traits.All(id=>eq.Traits.Any(t=>t.ID==id)),ed.id+" fixed traits");
                var save=new SRRWGameSave.EquipmentSave(new SRRWGameSave(game));eq.Save(save);var loaded=new Equipment(game);loaded.Load(save);
                Check(ed.traits.All(id=>loaded.Traits.Any(t=>t.ID==id)),ed.id+" saved fixed traits");fixedItems++;
            }
            foreach(var ed in data.EquipmentData.Values.Where(e=>e.randomTraitsByTagGroups.Any(added.Contains)))
            {
                Equipment eq=null;
                for(int sample=0;sample<3;sample++){
                    eq=new Equipment(game,ed);var generated=(List<Trait>)generatedField.GetValue(eq);
                    Check(generated.Count==ed.randomTraitsByTagGroups.Length,ed.id+" generated count");
                    for(int i=0;i<generated.Count;i++)Check(data.TagGroupData[ed.randomTraitsByTagGroups[i]].tagsHash.IsSubsetOf(generated[i].TraitData.equipmentGenerationTags),ed.id+" candidate eligibility");
                }
                var save=new SRRWGameSave.EquipmentSave(new SRRWGameSave(game));eq.Save(save);var loaded=new Equipment(game);loaded.Load(save);
                Check(((List<Trait>)generatedField.GetValue(eq)).Select(t=>t.ID).SequenceEqual(((List<Trait>)generatedField.GetValue(loaded)).Select(t=>t.ID)),ed.id+" stored rolls");
                save.SaveGeneratedTraits(new List<Trait>());loaded=new Equipment(game);loaded.Load(save);Check(((List<Trait>)generatedField.GetValue(loaded)).Count==0,ed.id+" old empty rolls");rolls++;
            }
            Check(fixedItems==336&&rolls==288,"item counts");
            ICard aegis=new CamaraderieCard(game,data.CamaraderieCardData["aegCard4"]);Check(aegis.Description=="Heals 10 health per round.\nLasts 2 combats.","Aegis text");
            Check(data.EffectData["carHealthOverTime10"].healPerRound==10,"Aegis effect");
            foreach(var card in data.CamaraderieCardData.Values.Where(c=>c.requiredAffection>0&&(c.GetTargetType()==CardTargetType.Any||c.GetTargetType()==CardTargetType.AnyOther||c.GetTargetType()==CardTargetType.ID)))
            {
                ICard actual=new CamaraderieCard(game,card);string expected=Mdi.SRRW.UI.UIStrings.GetRelationshipTargetDescription(game,card.requiredAffection);Check(actual.TargetDescription==expected,card.id+" base label");
                var reward=FormatterServices.GetUninitializedObject(PreviewFix.ResolveTargets()[1].DeclaringType);Set(reward,"game",game);Set(reward,"camaraderieCardData",card);Check(((ICard)reward).TargetDescription==expected,card.id+" reward label");labels++;
            }
            Check(labels==30,"rank label count");
            int paragonCases=0;
            foreach(string powerId in new[]{"hParSpinThrow","hParGroundStab","hParInterrupt"})foreach(int tier in new[]{1,2})foreach(bool gear in new[]{false,true})foreach(bool camping in new[]{false,true})
            {
                var unit=Fresh(game,data);var power=game.GetPower(powerId);var native=new UnitPower(unit,power);
                float baseCrit=native.CriticalTransferShieldAbsolute,baseNormal=native.TransferShieldAbsolute;
                string itemModifier=powerId=="hParSpinThrow"?"eqpFlurryShieldStealB2":powerId=="hParGroundStab"?"eqpAOEShieldStealB2":"eqpNormalShieldStealA1";
                if(gear)unit.EquipmentPowerModifiers.Add(new PowerModifier(game,data.PowerModifierData[itemModifier]));
                if(camping)unit.OtherPowerModifiers.Add(new PowerModifier(game,data.PowerModifierData["carDrainShields5"]));
                var corrected=data.PowerModifierData["camParTrait"+tier];
                var original=(PowerModifierData)Clone(corrected);original.setCriticalTransferShieldAbsolute=-5*tier;original.modCriticalTransferShieldAbsolute=0;
                unit.OtherPowerModifiers.Add(new PowerModifier(game,original));var before=new UnitPower(unit,power);
                Check(before.CriticalTransferShieldAbsolute==-5*tier,"original overwrite reproduced");
                unit.OtherPowerModifiers.RemoveAt(unit.OtherPowerModifiers.Count-1);unit.OtherPowerModifiers.Add(new PowerModifier(game,corrected));var after=new UnitPower(unit,power);
                Check(after.CriticalTransferShieldAbsolute==baseCrit-(gear?5:0)-(camping?5:0)-5*tier,"Paragon additive shield steal");Check(after.TransferShieldAbsolute==baseNormal,"non-CRIT shield steal unchanged");paragonCases++;
            }
            Check(ReflectionPreviewFix.ArmorDamageForEstimate(Fresh(game,data).Powers.First(),true)==0,"reflection helper active");Check(ReflectionPreviewFix.DelayForEstimate(7.5f)==7,"delay helper active");
            Doorstop.Entrypoint.Log("ACCEPTANCE PASSED: 336 fixed items and saved loads; 288 roll variants x3; stored/empty rolls preserved; Aegis text/effect; 30 base and reward labels; 24 Paragon combinations with original overwrite reproduction; prediction helpers active.");
        }
    }
}
