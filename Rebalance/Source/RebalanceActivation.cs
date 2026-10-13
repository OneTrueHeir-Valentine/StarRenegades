using System;
using System.Linq;
using Mdi.SRRW.Data;
using Newtonsoft.Json.Linq;

namespace SRRebalance
{
    public static class RebalanceActivation
    {
        public static SRBugfix.Overlay.StageResult Apply(GameData data,JObject coreManifest,JObject balanceManifest,Action[] installHooks,Action removeHooks)
        {
            Overlay.StageResult balance=null;bool balanceCommitted=false;
            Action applyBalance=()=>{
                balance=Overlay.Stage(data,balanceManifest);
                balance.Commit(data);balanceCommitted=true;
            };
            Action rollback=()=>{
                try{removeHooks();}
                finally{if(balanceCommitted)balance.Restore(data);}
            };
            var result=SRBugfix.Activation.Apply(data,coreManifest,new[]{applyBalance}.Concat(installHooks).ToArray(),rollback);
            result.ChangedFields+=balance.ChangedFields;result.AddedRecords+=balance.AddedRecords;
            return result;
        }
    }
}
