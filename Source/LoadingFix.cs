using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mdi.SRRW.Data;
using Mdi.SRRW.Engine;
using Mdi.SRRW.UI;
using UnityEngine;

namespace SRBugfix
{
    public static class LoadingFix
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly Type LoadState=typeof(TitleController).GetNestedType("TitleScreenLoadGameState",BindingFlags.NonPublic);
        static readonly Type SelectState=typeof(TitleController).GetNestedType("TitleScreenSelectSaveSlotState",BindingFlags.NonPublic);
        static readonly FieldInfo State=typeof(TitleController).GetField("state",Instance);
        static readonly FieldInfo Pending=typeof(GameController).GetField("pendingGame",Static);
        static readonly FieldInfo LastSave=typeof(GameController).GetField("lastSave",Static);
        static readonly MethodInfo PrepareSave=typeof(GameController).GetMethod("PrepareSaveForLoading",Static);
        static readonly MethodInfo Route=typeof(GameController).GetMethod("TransitionToAppropriateScene",Instance);
        static readonly MethodInfo ChangeState=typeof(TitleController).GetMethod("GoToState",Instance);
        static readonly Dictionary<object,Session> sessions=new Dictionary<object,Session>();
        static readonly LoadingQueue<SRRWGame> queue=new LoadingQueue<SRRWGame>();
        static bool enabled;
        public static Action<string> Report;
        public static bool Enabled {
            get{return enabled;}
            set{enabled=value;if(!value)foreach(var session in sessions.Values.ToArray())session.Cancel(false);}
        }
        public static MethodBase[] ResolveTargets()
        {
            if(LoadState==null||SelectState==null)throw new InvalidDataException("Unsupported title loading states.");
            return new MethodBase[]{LoadState.GetMethod("DoLoadGame",Instance),LoadState.GetMethod("Exit",Instance),
                typeof(GameController).GetMethod("LoadGameAsync",Instance,null,new[]{typeof(string),typeof(GameData),typeof(MapMetaData),typeof(bool)},null)};
        }
        public static void ValidateTargets()
        {
            var targets=ResolveTargets();
            if(targets.Any(x=>x==null||x.GetMethodBody()==null)||((MethodInfo)targets[0]).ReturnType!=typeof(IEnumerator)
                ||((MethodInfo)targets[1]).ReturnType!=typeof(void)||((MethodInfo)targets[2]).ReturnType!=typeof(IEnumerator)
                ||State==null||Pending==null||Pending.FieldType!=typeof(SRRWGame)||LastSave==null||LastSave.FieldType!=typeof(SRRWGameSave)
                ||PrepareSave==null||Route==null||ChangeState==null
                ||SelectState.GetConstructor(Instance,null,new[]{typeof(TitleController),typeof(int)},null)==null)
                throw new InvalidDataException("Unsupported title loading API.");
            foreach(string name in new[]{"inputListener","startingCam","transitionOnStartCam","companyLogos","saveSlotPanel","systemDialog"})
                if(typeof(TitleController).GetField(name,Instance)==null)throw new InvalidDataException("Missing title loading field: "+name);
            if(LoadState.BaseType.GetField("controller",Instance)==null||LoadState.GetField("slotIndex",Instance)==null
                ||typeof(ScreenTransitionController).GetField("transitionCount",Instance)==null
                ||typeof(ScreenTransitionController).GetField("loadingScreen",Instance)==null
                ||typeof(ScreenTransitionController).GetMethod("CheckTransitionOut",Instance)==null
                ||typeof(ScreenTransitionController).GetMethod("EndTransition",Instance)==null)
                throw new InvalidDataException("Unsupported title loading ownership fields.");
        }
        public static void Install(Harmony harmony)
        {
            ValidateTargets();var targets=ResolveTargets();
            harmony.Patch(targets[0],postfix:Hook("LoadStatePostfix"));
            harmony.Patch(targets[1],prefix:Hook("ExitPrefix"));
            harmony.Patch(targets[2],prefix:Hook("LoadPrefix"));
        }
        static HarmonyMethod Hook(string name){return new HarmonyMethod(typeof(LoadingFix).GetMethod(name,Static));}
        static void LoadStatePostfix(object __instance,ref IEnumerator __result)
        {
            if(!Enabled)return;
            if(sessions.TryGetValue(__instance,out var old))old.Cancel(false);
            var session=new Session(__instance);sessions[__instance]=session;
            __result=session.Wrap(__result);
        }
        static void ExitPrefix(object __instance)
        {if(sessions.TryGetValue(__instance,out var session))session.Cancel(true);}
        static bool LoadPrefix(GameController __instance,string __0,GameData __1,MapMetaData __2,bool __3,ref IEnumerator __result)
        {
            if(!Enabled)return true;
            var session=sessions.Values.FirstOrDefault(s=>ReferenceEquals(s.Game,__instance)&&s.Flow.Alive);
            if(session==null)return true;
            __result=session.Load(__0,__1,__2,__3);return false;
        }
        static T Field<T>(TitleController title,string name){return (T)typeof(TitleController).GetField(name,Instance).GetValue(title);}
        static object Invoke(MethodInfo method,object target,params object[] args)
        {try{return method.Invoke(target,args);}catch(TargetInvocationException error){throw error.InnerException??error;}}
        static void Log(Exception error){try{Report?.Invoke("Title load failed: "+error);}catch{}}

        internal sealed class Session
        {
            internal GameController Game;
            internal LoadingFlow Flow;
            internal LoadingFade Fade;
            readonly object state;
            readonly TitleController title;
            readonly TitleInputListener input;
            readonly Component startingCamera,transitionCamera;
            readonly AnimatingPanel logos;
            readonly SimpleDialogPopup dialog;
            readonly int slot;
            readonly bool uiActive,inputEnabled,startingActive,transitionActive,logosVisible;
            readonly LoadingLifetime lifetime;
            LoadingWork<SRRWGame> work;
            SRRWGameSave previousSave,preparedSave;
            Action confirm,dismiss;
            Action<ControllerSupport.GameInputScheme> scheme;
            bool suppressRestore,finished,dialogConnected,handoff;
            internal Session(object state)
            {
                this.state=state;
                title=(TitleController)LoadState.BaseType.GetField("controller",Instance).GetValue(state);
                Game=(GameController)typeof(GameMonoBehaviour).GetProperty("GameController",Instance).GetValue(title,null);
                slot=(int)LoadState.GetField("slotIndex",Instance).GetValue(state);
                input=Field<TitleInputListener>(title,"inputListener");dialog=Field<SimpleDialogPopup>(title,"systemDialog");
                startingCamera=Field<Component>(title,"startingCam");transitionCamera=Field<Component>(title,"transitionOnStartCam");
                logos=Field<AnimatingPanel>(title,"companyLogos");
                uiActive=input.IsUIActive;inputEnabled=input.enabled;
                startingActive=startingCamera.gameObject.activeSelf;transitionActive=transitionCamera.gameObject.activeSelf;logosVisible=logos.IsVisible;
                Flow=new LoadingFlow(IsCurrent,Restore,ShowFailure,CloseFailure,ReturnToSlots,Log,ReleaseFade,()=>Fade==null||Fade.Released);
                lifetime=title.GetComponent<LoadingLifetime>()??title.gameObject.AddComponent<LoadingLifetime>();
                lifetime.Current?.Cancel(false);lifetime.Current=this;
                Game.OnPrepareForSceneExit+=SceneExit;
            }
            bool IsCurrent(){return !suppressRestore&&Enabled&&title!=null&&title.isActiveAndEnabled&&Game!=null&&ReferenceEquals(State.GetValue(title),state);}
            void SceneExit(){Cancel(false);}
            internal IEnumerator Wrap(IEnumerator original)
            {
                try{yield return Flow.Run(original);}
                finally{Finish();}
            }
            internal void Cancel(bool restore)
            {
                if(!restore)suppressRestore=true;
                work?.Invalidate();Flow.Cancel();Finish();
            }
            void Finish()
            {
                if(finished)return;finished=true;
                work?.Invalidate();Flow.Cancel();
                if(Game!=null)Game.OnPrepareForSceneExit-=SceneExit;
                if(sessions.TryGetValue(state,out var current)&&ReferenceEquals(current,this))sessions.Remove(state);
                if(lifetime!=null&&ReferenceEquals(lifetime.Current,this))lifetime.Current=null;
            }
            internal IEnumerator Load(string filename,GameData data,MapMetaData metadata,bool abandon)
            {
                if(Pending.GetValue(null)!=null)throw new InvalidOperationException("Another load already has a pending game.");
                var screen=Game.ScreenTransitionController;
                if(screen==null)throw new InvalidOperationException("The title loading screen is unavailable.");
                Fade=new LoadingFade(()=>screen.FadeToLoadingScreenIn(0.5f),()=>screen.FadeToLoadingScreenOut(0.2f),
                    it=>{if(screen!=null)screen.StartCoroutine(it);},
                    ()=>(int)typeof(ScreenTransitionController).GetField("transitionCount",Instance).GetValue(screen),Log);
                Fade.RecoverRelease=leaseReleased=>RecoverFade(screen,leaseReleased);
                Fade.Begin();
                while(Flow.Alive&&!Fade.Ready)yield return null;
                if(!Flow.Alive)yield break;
                if(Fade.Error!=null)throw Fade.Error;
                yield return Reconstruct(filename,data,metadata,abandon);
            }
            internal IEnumerator Reconstruct(string filename,GameData data,MapMetaData metadata,bool abandon)
            {
                previousSave=(SRRWGameSave)LastSave.GetValue(null);
                Invoke(PrepareSave,null,filename,false);
                preparedSave=(SRRWGameSave)LastSave.GetValue(null);
                // Capture the prepared reference, never read static lastSave on a worker.
                var capturedSave=preparedSave;
                while(Flow.Alive&&(work=queue.TryStart(()=>new SRRWGame(data,metadata,capturedSave,abandon)))==null)yield return null;
                if(!Flow.Alive)yield break;
                while(Flow.Alive&&!work.Completion.IsCompleted)yield return null;
                if(!Flow.Alive)yield break;
                SRRWGame game;Exception error;
                if(!work.TryTake(out game,out error))yield break;
                if(error!=null)throw error;
                if(game==null)throw new InvalidDataException("Loading produced no game.");
                if(Pending.GetValue(null)!=null)throw new InvalidOperationException("A different load already published a game.");
                Pending.SetValue(null,game);Flow.Commit();
                try{Invoke(Route,Game);handoff=true;Fade?.Transfer();}
                catch{
                    Flow.UndoCommit();
                    if(ReferenceEquals(Pending.GetValue(null),game))Pending.SetValue(null,null);
                    throw;
                }
            }
            void ReleaseFade(){Fade?.Release();}
            IEnumerator RecoverFade(ScreenTransitionController screen,bool leaseReleased)
            {
                if(screen==null)yield break;
                // A failed visual fade may already have released the lease.
                // Repeat only the missing bookkeeping, never the decrement.
                if(!leaseReleased)yield return (IEnumerator)Invoke(typeof(ScreenTransitionController).GetMethod("CheckTransitionOut",Instance),screen);
                if(screen==null)yield break;
                int remaining=(int)typeof(ScreenTransitionController).GetField("transitionCount",Instance).GetValue(screen);
                if(remaining!=0)yield break;
                var panel=(AnimatingPanel)typeof(ScreenTransitionController).GetField("loadingScreen",Instance).GetValue(screen);
                try{if(panel!=null)panel.SetVisible(false,0f);}
                catch(Exception error){Log(error);if(panel!=null)panel.gameObject.SetActive(false);}
                yield return LoadingFade.FinishWhenUnowned(
                    (IEnumerator)Invoke(typeof(ScreenTransitionController).GetMethod("EndTransition",Instance),screen),
                    ()=>screen==null?1:(int)typeof(ScreenTransitionController).GetField("transitionCount",Instance).GetValue(screen));
            }
            void Restore()
            {
                if(!IsCurrent()||handoff)return;
                if(preparedSave!=null&&ReferenceEquals(LastSave.GetValue(null),preparedSave))LastSave.SetValue(null,previousSave);
                startingCamera.gameObject.SetActive(startingActive);transitionCamera.gameObject.SetActive(transitionActive);
                logos.SetVisible(logosVisible,0f);input.enabled=inputEnabled;input.IsUIActive=uiActive;
            }
            void ShowFailure(Action done)
            {
                if(!IsCurrent())return;
                dismiss=done;confirm=()=>{if(IsCurrent())dialog.SelectLeftButton();};
                scheme=value=>{if(IsCurrent()&&value!=ControllerSupport.GameInputScheme.KeyboardMouse)dialog.HighlightLeftButton();};
                dialogConnected=true;dialog.OnOption1Selected+=dismiss;
                input.OnConfirm+=confirm;input.OnBack+=confirm;input.OnInputSchemeChanged+=scheme;
                dialog.ConfigureAndShow("Unable to load game","The game could not load this save. Return to save selection and try again. Details are in the mod log.","Back",null);
                scheme(input.CurrentInputScheme);
            }
            void CloseFailure()
            {
                if(!dialogConnected)return;dialogConnected=false;
                input.OnConfirm-=confirm;input.OnBack-=confirm;input.OnInputSchemeChanged-=scheme;
                dialog.OnOption1Selected-=dismiss;
                if(dialog!=null)dialog.SetVisible(false,0f);
                confirm=null;dismiss=null;scheme=null;
            }
            void ReturnToSlots()
            {
                if(!IsCurrent())return;
                var selection=Activator.CreateInstance(SelectState,Instance,null,new object[]{title,slot},null);
                Invoke(ChangeState,title,selection);
            }
        }
    }

    public sealed class LoadingLifetime:MonoBehaviour
    {
        internal LoadingFix.Session Current;
        void OnDisable(){Current?.Cancel(false);}
        void OnDestroy(){Current?.Cancel(false);}
    }
}
