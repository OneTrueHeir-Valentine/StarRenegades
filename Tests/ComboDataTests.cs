using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mdi.SRRW.Data;

internal static class ComboDataTests
{
    static int passed,failed;
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string n=new AssemblyName(e.Name).Name+".dll",p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,n);
            if(!File.Exists(p))p=Path.Combine(args[0],"Star Renegades_Data/Managed",n);
            return File.Exists(p)?Assembly.LoadFrom(p):null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Test("StageDoesNotMutateLivePowerOrEnglish",()=>{var d=Fixture();var powers=d.PowerData;var text=d.PowerStringData;var effects=d.EffectData;Stage(d);Check(ReferenceEquals(powers,d.PowerData)&&ReferenceEquals(text,d.PowerStringData)&&ReferenceEquals(effects,d.EffectData),"stage changed dictionaries");Check(powers["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanEmpCombo"&&text["hGrdSpeComboDesc"].EN.Contains("recharge"),"stage changed rows");});
        Test("IlluminateDeactivatesItsOwnPowerAndKeepsOtherCombo",()=>{var d=Fixture();Commit(Stage(d),d);Check(d.PowerData["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanGueCombo","Illuminate still disables another combo");Check(d.PowerData["hVanEmpCombo"].deactivatesPowersOnUse[0]=="hVanEmpCombo","Exhilaration definition changed");});
        Test("HardResetEnglishPromisesOnlyExistingCleanses",()=>{var d=Fixture();Commit(Stage(d),d);Check(d.PowerStringData["hGrdSpeComboDesc"].EN=="Wipe all enemy buffs and all hero debuffs.","Hard Reset still promises nonexistent recharge");Check(d.PowerStringData["hGrdSpeComboDesc"].FR=="keep French","other language changed");});
        Test("OnlyEditedRowsClonedAndOtherArraysKept",()=>{var d=Fixture();var p=d.PowerData["hVanGueCombo"];var other=d.PowerData["hVanEmpCombo"];var tags=p.tags;Commit(Stage(d),d);Check(!ReferenceEquals(p,d.PowerData[p.id])&&ReferenceEquals(other,d.PowerData[other.id]),"row cloning scope differs");Check(ReferenceEquals(tags,d.PowerData[p.id].tags)&&d.PowerData[p.id].squadFocusCost==50,"unrelated mechanic changed");Check(p.deactivatesPowersOnUse[0]=="hVanEmpCombo","original changed");});
        Test("RestoreReturnsBothExactOriginalDictionaries",()=>{var d=Fixture();var powers=d.PowerData;var texts=d.PowerStringData;var effects=d.EffectData;var stage=Stage(d);Commit(stage,d);Call(stage,"Restore",d);Check(ReferenceEquals(powers,d.PowerData)&&ReferenceEquals(texts,d.PowerStringData)&&ReferenceEquals(effects,d.EffectData),"restore failed");});
        Test("UnexpectedLastEnglishGuardLeavesBothTablesUnchanged",()=>{var d=Fixture();d.PowerStringData["hGrdSpeComboDesc"].EN="edited";var p=d.PowerData;var t=d.PowerStringData;Reject(()=>Stage(d));Check(ReferenceEquals(p,d.PowerData)&&ReferenceEquals(t,d.PowerStringData)&&p["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanEmpCombo","rejected stage changed data");});
        Test("UnexpectedDeactivationGuardRejected",()=>{var d=Fixture();d.PowerData["hVanGueCombo"].deactivatesPowersOnUse=new[]{"edited"};Reject(()=>Stage(d));});
        Test("MissingPowerRejected",()=>{var d=Fixture();d.PowerData.Remove("hVanGueCombo");Reject(()=>Stage(d));});
        Test("MissingTextRejected",()=>{var d=Fixture();d.PowerStringData.Remove("hGrdSpeComboDesc");Reject(()=>Stage(d));});
        Test("ConcurrentReplacementPreserved",()=>{var d=Fixture();var stage=Stage(d);var changed=new Dictionary<string,LocalizedStringData>(d.PowerStringData);Set(d,"PowerStringData",changed);Reject(()=>Commit(stage,d));Check(ReferenceEquals(changed,d.PowerStringData)&&d.PowerData["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanEmpCombo","commit overwrote concurrent data");});
        Test("DifferentOwnerRejected",()=>{var d=Fixture();var stage=Stage(d);Reject(()=>Commit(stage,Fixture()));Reject(()=>Call(stage,"Restore",Fixture()));});
        Test("ThreeChangedFieldsReported",()=>{var s=Stage(Fixture());Check((int)s.GetType().GetField("ChangedFields").GetValue(s)==3,"wrong field count");});
        Test("RepeatStageRejected",()=>{var d=Fixture();Commit(Stage(d),d);Reject(()=>Stage(d));});
        Test("ShadowInstantExpiresOnAttackingTagWithoutChangingDuration",()=>{var d=Fixture();var effect=d.EffectData["hDarSpeCombo"];Commit(Stage(d),d);var after=d.EffectData["hDarSpeCombo"];Check(!ReferenceEquals(effect,after)&&effect.tags.Length==0,"original effect mutated");Check(after.tags.Length==1&&after.tags[0]=="wipedByAttacking"&&after.tagsHash.Contains("wipedByAttacking"),"outgoing cancellation hash missing");Check(after.duration==-1&&after.cancelledAfterNumAttacks==1&&after.requiredUnitTags[0]=="hDarUnit","other expiry or eligibility changed");});
        Test("UnexpectedShadowTagsRejectsBeforeAnyCommit",()=>{var d=Fixture();d.EffectData["hDarSpeCombo"].tags=new[]{"edited"};var powers=d.PowerData;Reject(()=>Stage(d));Check(ReferenceEquals(powers,d.PowerData)&&powers["hVanGueCombo"].deactivatesPowersOnUse[0]=="hVanEmpCombo","rejection changed other rows");});
        Test("MissingShadowEffectRejected",()=>{var d=Fixture();d.EffectData.Remove("hDarSpeCombo");Reject(()=>Stage(d));});
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static GameData Fixture()
    {
        var d=(GameData)FormatterServices.GetUninitializedObject(typeof(GameData));
        Set(d,"PowerData",new Dictionary<string,PowerData>{
            {"hVanGueCombo",new PowerData{id="hVanGueCombo",deactivatesPowersOnUse=new[]{"hVanEmpCombo"},tags=new[]{"combo","attack"},squadFocusCost=50}},
            {"hVanEmpCombo",new PowerData{id="hVanEmpCombo",deactivatesPowersOnUse=new[]{"hVanEmpCombo"}}}
        });
        Set(d,"PowerStringData",new Dictionary<string,LocalizedStringData>{
            {"hGrdSpeComboDesc",new LocalizedStringData{id="hGrdSpeComboDesc",EN="Wipe all enemy buffs, wipe all hero debuffs, recharge all hero shields.",FR="keep French"}}
        });
        var effect=new EffectData{id="hDarSpeCombo",tags=new string[0],duration=-1,cancelledAfterNumAttacks=1,requiredUnitTags=new[]{"hDarUnit"}};
        foreach(var f in typeof(EffectData).GetFields())if(f.FieldType==typeof(string[])&&f.GetValue(effect)==null)f.SetValue(effect,new string[0]);
        ((UnityEngine.ISerializationCallbackReceiver)effect).OnAfterDeserialize();
        Set(d,"EffectData",new Dictionary<string,EffectData>{{effect.id,effect}});return d;
    }
    static void Set(GameData d,string n,object v){typeof(GameData).GetProperty(n).GetSetMethod(true).Invoke(d,new[]{v});}
    static object Stage(GameData d){var t=typeof(ComboDataTests).Assembly.GetType("SRBugfix.ComboDataFix");Check(t!=null,"combo data fixes missing");return Call(t,"Stage",d);}
    static void Commit(object s,GameData d){Call(s,"Commit",d);}
    static object Call(object t,string n,params object[] a){try{return (t is Type?(Type)t:t.GetType()).GetMethod(n,Flags).Invoke(t is Type?null:t,a);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Reject(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("invalid data accepted");}
    static void Check(bool c,string m){if(!c)throw new Exception(m);}
    static void Test(string n,Action a){try{a();passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.Message);}}
}
