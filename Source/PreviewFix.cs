using System;
using System.Reflection;
using System.IO;
using System.Linq;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Mdi.SRRW.Engine.Camaraderie;
using Mdi.SRRW.UI;
using EngineCard=Mdi.SRRW.Engine.Camaraderie.CamaraderieCard;
namespace SRBugfix
{
    public static class PreviewFix
    {
        static FieldInfo rewardGame,rewardData;
        internal static bool Enabled=false;
        public static string CorrectLabel(SRRWGame game,CamaraderieCardData card,string currentLabel)
        {
            switch(card.GetTargetType())
            {
                case CardTargetType.Any:case CardTargetType.AnyOther:case CardTargetType.ID:
                    return UIStrings.GetRelationshipTargetDescription(game,card.requiredAffection);
                default:return currentLabel;
            }
        }
        public static MethodInfo[] ResolveTargets(){return ResolveTargets(typeof(EngineCard),typeof(CamaraderieManeuverManager).GetNestedType("CamaraderieManeuverRewardCard",BindingFlags.NonPublic));}
        internal static MethodInfo[] ResolveTargets(Type baseCard,Type rewardCard)
        {
            var methods=new[]{Getter(baseCard),Getter(rewardCard)};
            var game=rewardCard.GetField("game",BindingFlags.Instance|BindingFlags.NonPublic);
            var data=rewardCard.GetField("camaraderieCardData",BindingFlags.Instance|BindingFlags.NonPublic);
            if(baseCard!=typeof(EngineCard)||game==null||game.FieldType!=typeof(SRRWGame)||data==null||data.FieldType!=typeof(CamaraderieCardData))throw new InvalidDataException("card preview fields differ from the supported build");
            rewardGame=game;rewardData=data;return methods;
        }
        static MethodInfo Getter(Type type)
        {
            if(type==null||!typeof(ICard).IsAssignableFrom(type))throw new InvalidDataException("card preview getter is missing");
            var map=type.GetInterfaceMap(typeof(ICard));int index=Array.FindIndex(map.InterfaceMethods,m=>m.Name=="get_TargetDescription");
            if(index<0||map.TargetMethods[index].ReturnType!=typeof(string)||map.TargetMethods[index].GetParameters().Length!=0)throw new InvalidDataException("card preview getter differs from the supported build");
            return map.TargetMethods[index];
        }
        internal static void BasePostfix(EngineCard __instance,ref string __result){if(Enabled)__result=CorrectLabel(__instance.Game,__instance.CamaraderieCardData,__result);}
        internal static void RewardPostfix(object __instance,ref string __result){if(Enabled)__result=CorrectLabel((SRRWGame)rewardGame.GetValue(__instance),(CamaraderieCardData)rewardData.GetValue(__instance),__result);}
    }
}
