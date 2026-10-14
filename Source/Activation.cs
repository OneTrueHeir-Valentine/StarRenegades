using System;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
namespace SRBugfix
{
    public static class Activation
    {
        public static Overlay.StageResult Apply(GameData data,JObject manifest,Action[] installHooks,Action removeHooks)
        {
            Overlay.StageResult stage=null;EnemyDataFix.StageResult enemies=null;ComboDataFix.StageResult combos=null;
            bool committed=false,enemyCommitted=false,comboCommitted=false;
            try
            {
                stage=Overlay.Stage(data,manifest);enemies=EnemyDataFix.Stage(data);combos=ComboDataFix.Stage(data);
                stage.Commit(data);committed=true;enemies.Commit(data);enemyCommitted=true;combos.Commit(data);comboCommitted=true;
                foreach(var hook in installHooks)hook();stage.ChangedFields+=enemies.ChangedFields+combos.ChangedFields;return stage;
            }
            catch
            {
                try{removeHooks();}finally{
                    try{if(comboCommitted)combos.Restore(data);}
                    finally{try{if(enemyCommitted)enemies.Restore(data);}finally{if(committed)stage.Restore(data);}}
                }
                throw;
            }
        }
    }
}
