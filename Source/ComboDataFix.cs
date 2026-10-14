using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mdi.SRRW.Data;

namespace SRBugfix
{
    public static class ComboDataFix
    {
        const string OldHardReset="Wipe all enemy buffs, wipe all hero debuffs, recharge all hero shields.";
        static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        public sealed class StageResult
        {
            public int ChangedFields=3;
            readonly GameData owner;
            readonly Dictionary<string,PowerData> powersBefore,powersAfter;
            readonly Dictionary<string,LocalizedStringData> textBefore,textAfter;
            readonly Dictionary<string,EffectData> effectsBefore,effectsAfter;
            internal StageResult(GameData data,Dictionary<string,PowerData> powers,Dictionary<string,LocalizedStringData> text,Dictionary<string,EffectData> effects)
            {owner=data;powersBefore=data.PowerData;textBefore=data.PowerStringData;effectsBefore=data.EffectData;powersAfter=powers;textAfter=text;effectsAfter=effects;}
            public void Commit(GameData data)
            {
                if(!ReferenceEquals(data,owner)||!ReferenceEquals(data.PowerData,powersBefore)||!ReferenceEquals(data.PowerStringData,textBefore)||!ReferenceEquals(data.EffectData,effectsBefore))
                    throw new InvalidDataException("Combo data changed since staging.");
                var powerSetter=Setter("PowerData");var textSetter=Setter("PowerStringData");var effectSetter=Setter("EffectData");
                try{powerSetter.Invoke(data,new object[]{powersAfter});textSetter.Invoke(data,new object[]{textAfter});effectSetter.Invoke(data,new object[]{effectsAfter});}
                catch{Restore(data);throw;}
            }
            public void Restore(GameData data)
            {
                if(!ReferenceEquals(data,owner))throw new InvalidDataException("Combo rollback belongs to a different data instance.");
                Setter("PowerData").Invoke(data,new object[]{powersBefore});Setter("PowerStringData").Invoke(data,new object[]{textBefore});Setter("EffectData").Invoke(data,new object[]{effectsBefore});
            }
            static MethodInfo Setter(string name)
            {
                var setter=typeof(GameData).GetProperty(name).GetSetMethod(true);
                if(setter==null)throw new InvalidDataException("Combo table setter is missing: "+name);
                return setter;
            }
        }
        public static StageResult Stage(GameData data)
        {
            if(data==null||data.PowerData==null||data.PowerStringData==null||data.EffectData==null)throw new InvalidDataException("Combo data is missing.");
            PowerData illuminate;LocalizedStringData hardReset;EffectData shadow;
            if(!data.PowerData.TryGetValue("hVanGueCombo",out illuminate)||illuminate==null||illuminate.deactivatesPowersOnUse==null||
                !illuminate.deactivatesPowersOnUse.SequenceEqual(new[]{"hVanEmpCombo"}))throw new InvalidDataException("Illuminate baseline differs.");
            if(!data.PowerStringData.TryGetValue("hGrdSpeComboDesc",out hardReset)||hardReset==null||hardReset.EN!=OldHardReset)
                throw new InvalidDataException("Hard Reset English baseline differs.");
            if(!data.EffectData.TryGetValue("hDarSpeCombo",out shadow)||shadow==null||shadow.tags==null||shadow.tags.Length!=0)
                throw new InvalidDataException("Shadow Puppets effect baseline differs.");
            var powers=new Dictionary<string,PowerData>(data.PowerData);var strings=new Dictionary<string,LocalizedStringData>(data.PowerStringData);
            var corrected=(PowerData)Clone.Invoke(illuminate,null);corrected.deactivatesPowersOnUse=new[]{"hVanGueCombo"};powers[corrected.id]=corrected;
            var description=(LocalizedStringData)Clone.Invoke(hardReset,null);description.EN="Wipe all enemy buffs and all hero debuffs.";strings[description.id]=description;
            var effects=new Dictionary<string,EffectData>(data.EffectData);
            var instant=(EffectData)Clone.Invoke(shadow,null);instant.tags=new[]{"wipedByAttacking"};
            ((UnityEngine.ISerializationCallbackReceiver)instant).OnAfterDeserialize();effects[instant.id]=instant;
            return new StageResult(data,powers,strings,effects);
        }
    }
}
