using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SRBugfix
{
    public static class RuntimeFixes
    {
        const BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
        public static Action[] Prepare(Harmony harmony,Action<string> report)
        {
            AchievementFix.Report=report;
            var preview=PreviewFix.ResolveTargets();
            var reflection=ReflectionPreviewFix.ResolveTarget();var inspection=DamageInspectionFix.ResolveTarget();
            ReflectionPreviewFix.Transpile(PatchProcessor.GetOriginalInstructions(reflection)).ToArray();
            DamageInspectionFix.Transpile(PatchProcessor.GetOriginalInstructions(inspection)).ToArray();
            ProjectedEffectFix.ValidateTargets();ReflectionPreviewFix.ValidateReactionTargets();EnemyAiFix.ValidateTargets();
            var achievements=AchievementFix.ResolveTargets();
            return new Action[]{
                ()=>harmony.Patch(preview[0],postfix:Hook(typeof(PreviewFix),"BasePostfix")),
                ()=>harmony.Patch(preview[1],postfix:Hook(typeof(PreviewFix),"RewardPostfix")),
                ()=>harmony.Patch(reflection,transpiler:Hook(typeof(ReflectionPreviewFix),"Transpile")),
                ()=>harmony.Patch(inspection,transpiler:Hook(typeof(DamageInspectionFix),"Transpile")),
                ()=>ProjectedEffectFix.Install(harmony),
                ()=>ReflectionPreviewFix.InstallReactionHooks(harmony),
                ()=>EnemyAiFix.Install(harmony),
                ()=>harmony.Patch(achievements[0],postfix:Hook(typeof(AchievementFix),"InitializePostfix")),
                ()=>harmony.Patch(achievements[1],postfix:Hook(typeof(AchievementFix),"RecordPostfix")),
                ()=>harmony.Patch(achievements[2],prefix:Hook(typeof(AchievementFix),"AchievementPrefix")),
                ()=>harmony.Patch(achievements[3],prefix:Hook(typeof(AchievementFix),"UpdatePrefix"),postfix:Hook(typeof(AchievementFix),"UpdatePostfix")),
                ()=>harmony.Patch(achievements[4],postfix:Hook(typeof(AchievementFix),"ReceivedPostfix")),
                ()=>harmony.Patch(achievements[5],postfix:Hook(typeof(AchievementFix),"StoredPostfix")),
                ()=>harmony.Patch(achievements[6],postfix:Hook(typeof(AchievementFix),"EnablePostfix")),
                ()=>harmony.Patch(achievements[7],postfix:Hook(typeof(AchievementFix),"DisablePostfix"))
            };
        }
        static HarmonyMethod Hook(Type type,string name){return new HarmonyMethod(type.GetMethod(name,Flags));}
        public static void SetEnabled(bool value)
        {
            PreviewFix.Enabled=value;ReflectionPreviewFix.Enabled=value;DamageInspectionFix.Enabled=value;
            ProjectedEffectFix.Enabled=value;AchievementFix.Enabled=value;EnemyAiFix.Enabled=value;
        }
        public static void Remove(Harmony harmony){SetEnabled(false);harmony.UnpatchAll(harmony.Id);}
    }
}
