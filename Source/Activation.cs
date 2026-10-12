using System;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;
namespace SRBugfix
{
    public static class Activation
    {
        public static Overlay.StageResult Apply(GameData data,JObject manifest,Action[] installHooks,Action removeHooks)
        {
            Overlay.StageResult stage=null;bool committed=false;
            try
            {
                stage=Overlay.Stage(data,manifest);stage.Commit(data);committed=true;
                foreach(var hook in installHooks)hook();return stage;
            }
            catch
            {
                try{removeHooks();}finally{if(committed)stage.Restore(data);}
                throw;
            }
        }
    }
}
