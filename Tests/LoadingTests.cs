using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

internal static class LoadingTests
{
    static int passed,failed;
    static Type workType,sequenceType;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            var name=new AssemblyName(e.Name).Name+".dll";
            var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);
            return Assembly.LoadFrom(File.Exists(path)?path:Path.Combine(args[0],"Star Renegades_Data/Managed",name));
        };
        Test("WorkerFailureHasAnExplicitTerminalOutcome",()=>{
            // Removing the error outcome must leave this assertion failing,
            // rather than recreate the native endless pendingGame wait.
            workType=typeof(LoadingTests).Assembly.GetType("SRBugfix.LoadingWork`1");
            Check(workType!=null,"loading worker has no terminal error channel");
            var work=NewWork(()=>{throw new InvalidDataException("synthetic reconstruction failure");});
            Wait(work);
            object value,error;
            Check(Take(work,out value,out error)&&error is InvalidDataException&&value==null,"worker fault was not returned");
            Check(!Take(work,out value,out error),"terminal outcome consumed twice");
        });
        Test("SuccessfulWorkerPublishesOnlyThroughCaller",()=>{
            int publications=0;var work=NewWork(()=>"loaded");Wait(work);
            Check(publications==0,"worker published outside caller");object value,error;
            Check(Take(work,out value,out error)&&error==null&&(string)value=="loaded","success outcome lost");
            publications++;Check(!Take(work,out value,out error)&&publications==1,"success consumed twice");
        });
        Test("InvalidatedWorkerCannotDeliverLateSuccess",()=>{
            using(var release=new ManualResetEventSlim()){
                var work=NewWork(()=>{release.Wait();return "stale";});Call(work,"Invalidate");
                release.Set();Wait(work);object value,error;Check(!Take(work,out value,out error),"invalidated result deliverable");
            }
        });
        Test("InvalidatedWorkerStillObservesItsException",()=>{
            using(var release=new ManualResetEventSlim()){
                var work=NewWork(()=>{release.Wait();throw new InvalidDataException("late failure");});Call(work,"Invalidate");release.Set();Wait(work);
                var task=(Task)work.GetType().GetProperty("Completion",Flags).GetValue(work,null);
                Check(task.Status==TaskStatus.RanToCompletion,"late worker task faulted");object value,error;
                Check(!Take(work,out value,out error),"late failure actionable");
            }
        });
        Test("NestedIteratorExceptionTerminatesOwnedSequence",()=>{
            int disposed=0;var sequence=Sequence(Parent(new ThrowingIterator(()=>disposed++)));
            Check(!sequence.MoveNext(),"nested fault escaped as an indefinite wait");
            Check(Error(sequence) is InvalidDataException&&disposed==1,"fault or child cleanup lost");
        });
        Test("HealthyYieldsAndChildFinalValuesArePreserved",()=>{
            var child=Values();var sequence=Sequence(Parent(child));
            Check(sequence.MoveNext()&&(string)sequence.Current=="one","first child yield lost");
            Check(sequence.MoveNext()&&sequence.Current==null,"frame wait lost");
            Check(sequence.MoveNext()&&(string)sequence.Current=="one","last child yield lost");
            Check(!sequence.MoveNext()&&Error(sequence)==null,"healthy sequence failed");
            Check((string)child.Current=="one","native loader final Current changed");
        });
        Test("DisposeReleasesEveryOwnedIteratorOnce",()=>{
            int parentDisposed=0,childDisposed=0;
            var sequence=Sequence(ParentWithFinally(new WaitingIterator(()=>childDisposed++),()=>parentDisposed++));
            Check(sequence.MoveNext(),"did not enter child");((IDisposable)sequence).Dispose();((IDisposable)sequence).Dispose();
            Check(parentDisposed==1&&childDisposed==1&&!sequence.MoveNext(),"iterator ownership not released once");
        });
        Test("CleanupFailureDoesNotPreventRemainingDisposal",()=>{
            int parentDisposed=0;var sequence=Sequence(ParentWithFinally(new ThrowingIterator(()=>{throw new Exception("cleanup");}),()=>parentDisposed++));
            Check(!sequence.MoveNext()&&Error(sequence) is InvalidDataException&&parentDisposed==1,"cleanup masked original fault or leaked parent");
        });
        Test("LoadingErrorRestoresBeforeOfferingRetry",()=>{
            var fixture=new FlowFixture();var flow=fixture.Create();var run=fixture.Run(flow,new ThrowingIterator(()=>{}));
            Check(run.MoveNext()&&fixture.Restored==0&&fixture.Dismiss==null,"recovery ignored loading overlay ownership");
            fixture.Released=true;
            Check(run.MoveNext()&&fixture.Restored==1&&fixture.Dismiss!=null&&fixture.Errors==1,"error did not restore title and offer dismissal");
            fixture.Dismiss();Check(!run.MoveNext()&&fixture.Returned==1&&fixture.Closed==1,"dismissal did not return once");
            fixture.Dismiss();Check(!run.MoveNext()&&fixture.Returned==1,"duplicate dismissal repeated return");
        });
        Test("OwnerExitRejectsLateFailureAndDialogInput",()=>{
            var fixture=new FlowFixture();fixture.Released=true;var flow=fixture.Create();var run=fixture.Run(flow,new ThrowingIterator(()=>{}));
            Check(run.MoveNext()&&fixture.Dismiss!=null,"no failure dialog");
            fixture.Owns=false;fixture.Dismiss();Check(!run.MoveNext()&&fixture.Returned==0,"stale dialog changed state");
        });
        Test("InterruptedHealthyLoadReleasesOnlyOwnedResources",()=>{
            var fixture=new FlowFixture();var flow=fixture.Create();int disposed=0;
            var run=fixture.Run(flow,new WaitingIterator(()=>disposed++));Check(run.MoveNext(),"no wait");
            Call(flow,"Cancel");Check(!run.MoveNext()&&disposed==1&&fixture.ReleaseCalls==1&&fixture.Returned==0,"cancel leaked iterator or advanced selection");
        });
        Test("SuccessfulHandoffRetainsNativeFadeOwnership",()=>{
            var fixture=new FlowFixture();var flow=fixture.Create();Call(flow,"Commit");var run=fixture.Run(flow,Values());while(run.MoveNext()){}
            Check(fixture.ReleaseCalls==0&&fixture.Restored==0&&fixture.Returned==0&&fixture.Dismiss==null,"success ran recovery");
        });
        Test("ReplacementWaitsForInvalidatedConstructorToFinish",()=>{
            var type=typeof(LoadingTests).Assembly.GetType("SRBugfix.LoadingQueue`1");Check(type!=null,"constructors can overlap");
            var queue=Activator.CreateInstance(type.MakeGenericType(typeof(string)),true);
            using(var gate=new ManualResetEventSlim()){
                var first=TryStart(queue,()=>{gate.Wait();return "old";});Call(first,"Invalidate");
                try{Check(TryStart(queue,()=>"new")==null,"replacement constructor overlapped old constructor");}finally{gate.Set();}
                Wait(first);var second=TryStart(queue,()=>"new");Check(second!=null,"completed constructor blocked retry");Wait(second);
                object value,error;Check(Take(second,out value,out error)&&(string)value=="new"&&error==null,"retry result lost");
            }
        });
        Test("CompletedQueueDoesNotRetainThePreviousGame",()=>{
            var queue=new SRBugfix.LoadingQueue<string>();var work=queue.TryStart(()=>"finished game");Wait(work);
            var last=queue.GetType().GetField("last",Flags);
            Check(SpinWait.SpinUntil(()=>last.GetValue(queue)==null,1000),"completed game retained by the global loading queue");
        });
        Test("CancelledFadeFinishesBeforeReleasingItsSingleLease",()=>{
            var fixture=new FadeFixture(1);var fade=fixture.Create();Call(fade,"Begin");
            Check(fixture.Step()&&fixture.Count==2,"owned in did not acquire once");
            Call(fade,"Release");Call(fade,"Release");
            fixture.Drain();Check(fixture.Count==1&&fixture.OutCalls==1&&fixture.Released(fade),"cancel damaged another transition owner");
        });
        Test("SuccessfulFadeTransfersLeaseToNativeSceneRouting",()=>{
            var fixture=new FadeFixture(0);var fade=fixture.Create();Call(fade,"Begin");fixture.Drain();
            Call(fade,"Transfer");Call(fade,"Release");fixture.Drain();
            Check(fixture.Count==1&&fixture.OutCalls==0,"success prematurely released native loading cover");
        });
        Test("CancelledBeforeScheduledFadeDoesNotAcquire",()=>{
            var fixture=new FadeFixture(0);var fade=fixture.Create();Call(fade,"Begin");Call(fade,"Release");fixture.Drain();
            Check(fixture.Count==0&&fixture.OutCalls==0&&fixture.Released(fade),"unused fade altered transition count");
        });
        Test("FadeOutFaultAfterDecrementIsTerminalWithoutDoubleRelease",()=>{
            var fixture=new FadeFixture(0);fixture.FailOut=true;var fade=fixture.Create();Call(fade,"Begin");fixture.Drain();Call(fade,"Release");fixture.Drain();
            Check(fixture.Count==0&&fixture.OutCalls==1&&fixture.Released(fade)&&fixture.Errors==1,"faulted release recreated an endless loading wait");
        });
        Test("FallbackDoesNotClearAReplacementTransitionsFlag",()=>{
            var method=typeof(SRBugfix.LoadingFade).GetMethod("FinishWhenUnowned",BindingFlags.Static|BindingFlags.NonPublic);
            Check(method!=null,"fallback completion has no ownership guard");
            int count=0;bool inProgress=true;
            var finish=(IEnumerator)method.Invoke(null,new object[]{EndFlag(()=>inProgress=false),(Func<int>)(()=>count)});
            Check(finish.MoveNext()&&inProgress,"native final frame missing");count=1;
            Check(!finish.MoveNext()&&inProgress,"old recovery cleared newer transition flag");
        });
        Test("FallbackCompletesItsOwnTransitionWhenNoNewOwnerArrives",()=>{
            var method=typeof(SRBugfix.LoadingFade).GetMethod("FinishWhenUnowned",BindingFlags.Static|BindingFlags.NonPublic);
            Check(method!=null,"fallback completion has no ownership guard");bool inProgress=true;
            var finish=(IEnumerator)method.Invoke(null,new object[]{EndFlag(()=>inProgress=false),(Func<int>)(()=>0)});
            Check(finish.MoveNext()&&!finish.MoveNext()&&!inProgress,"owned fallback left transition active");
        });
        Test("SharedActivationOwnsLoadingHooksAndDisableRestoresNativePath",()=>{
            var type=typeof(LoadingTests).Assembly.GetType("SRBugfix.LoadingFix");Check(type!=null,"shared loading hooks missing");
            type.GetMethod("ValidateTargets",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);
            var harmony=new Harmony("tests.loading.hooks");
            try{
                type.GetMethod("Install",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{harmony});
                var targets=(MethodBase[])type.GetMethod("ResolveTargets",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);
                foreach(var target in targets)Check(Harmony.GetPatchInfo(target).Owners.Contains(harmony.Id),"loading target not installed");
                type.GetProperty("Enabled",BindingFlags.Static|BindingFlags.Public).SetValue(null,false,null);
                var prefix=type.GetMethod("LoadPrefix",BindingFlags.Static|BindingFlags.NonPublic);
                var hookArgs=new object[]{null,"fixture",null,null,false,null};
                Check((bool)prefix.Invoke(null,hookArgs)&&hookArgs[5]==null,"disabled helper replaced native load");
            }finally{harmony.UnpatchAll(harmony.Id);}
        });
        LoadingNativeTests.Run(Test);
        LoadingDiagnosticTests.Run(Test);
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static object NewWork(Func<string> create){Check(workType!=null,"worker unavailable");return Activator.CreateInstance(workType.MakeGenericType(typeof(string)),Flags,null,new object[]{create},null);}
    static void Wait(object work){var task=(Task)work.GetType().GetProperty("Completion",Flags).GetValue(work,null);Check(task.Wait(5000),"worker did not finish");}
    static bool Take(object work,out object value,out object error){var args=new object[]{null,null};bool result=(bool)work.GetType().GetMethod("TryTake",Flags).Invoke(work,args);value=args[0];error=args[1];return result;}
    static void Call(object target,string name){target.GetType().GetMethod(name,Flags).Invoke(target,null);}
    static IEnumerator Sequence(IEnumerator original){sequenceType=typeof(LoadingTests).Assembly.GetType("SRBugfix.LoadingSequence");Check(sequenceType!=null,"nested loading errors are not owned");return (IEnumerator)Activator.CreateInstance(sequenceType,Flags,null,new object[]{original},null);}
    static Exception Error(IEnumerator sequence){return (Exception)sequenceType.GetProperty("Error",Flags).GetValue(sequence,null);}
    static IEnumerator Parent(IEnumerator child){yield return child;}
    static IEnumerator ParentWithFinally(IEnumerator child,Action onDispose){try{yield return child;}finally{onDispose();}}
    static IEnumerator Values(){yield return "one";yield return null;yield return "one";}
    static IEnumerator EndFlag(Action finish){yield return null;finish();}
    static object TryStart(object queue,Func<string> create){return queue.GetType().GetMethod("TryStart",Flags).Invoke(queue,new object[]{create});}
    sealed class FadeFixture
    {
        internal int Count,OutCalls,Errors;internal bool FailOut;readonly System.Collections.Generic.Queue<IEnumerator> scheduled=new System.Collections.Generic.Queue<IEnumerator>();IEnumerator active;
        internal FadeFixture(int count){Count=count;}
        internal object Create(){var type=typeof(LoadingTests).Assembly.GetType("SRBugfix.LoadingFade");Check(type!=null,"loading fades have no cancellation owner");return Activator.CreateInstance(type,Flags,null,new object[]{(Func<IEnumerator>)(()=>In()),(Func<IEnumerator>)(()=>Out()),(Action<IEnumerator>)(it=>scheduled.Enqueue(it)),(Func<int>)(()=>Count),(Action<Exception>)(e=>Errors++)},null);}
        IEnumerator In(){Count++;yield return null;yield return null;}
        IEnumerator Out(){Count--;OutCalls++;if(FailOut)throw new InvalidDataException("synthetic fade-out failure");yield return null;}
        internal bool Step(){if(active==null&&scheduled.Count>0)active=scheduled.Dequeue();if(active==null)return false;if(!active.MoveNext())active=null;return true;}
        internal void Drain(){for(int n=0;n<100&&Step();n++){}Check(active==null&&scheduled.Count==0,"fade did not finish");}
        internal bool Released(object fade){return (bool)fade.GetType().GetProperty("Released",Flags).GetValue(fade,null);}
    }
    sealed class FlowFixture
    {
        internal bool Owns=true,Released;internal int Restored,Closed,Returned,Errors,ReleaseCalls;internal Action Dismiss;
        internal object Create(){var type=typeof(LoadingTests).Assembly.GetType("SRBugfix.LoadingFlow");Check(type!=null,"title recovery has no state owner");
            return Activator.CreateInstance(type,Flags,null,new object[]{(Func<bool>)(()=>Owns),(Action)(()=>Restored++),(Action<Action>)(d=>Dismiss=d),(Action)(()=>Closed++),(Action)(()=>Returned++),(Action<Exception>)(e=>Errors++),(Action)(()=>ReleaseCalls++),(Func<bool>)(()=>Released)},null);}
        internal IEnumerator Run(object flow,IEnumerator original){return (IEnumerator)flow.GetType().GetMethod("Run",Flags).Invoke(flow,new object[]{original});}
    }
    sealed class ThrowingIterator:IEnumerator,IDisposable{readonly Action dispose;public ThrowingIterator(Action d){dispose=d;}public object Current{get{return null;}}public bool MoveNext(){throw new InvalidDataException("child load failed");}public void Reset(){throw new NotSupportedException();}public void Dispose(){dispose();}}
    sealed class WaitingIterator:IEnumerator,IDisposable{readonly Action dispose;public WaitingIterator(Action d){dispose=d;}public object Current{get{return null;}}public bool MoveNext(){return true;}public void Reset(){throw new NotSupportedException();}public void Dispose(){dispose();}}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
}
