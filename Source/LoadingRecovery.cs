using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace SRBugfix
{
    internal sealed class LoadingQueue<T>
    {
        LoadingWork<T> last;
        readonly object gate=new object();
        internal LoadingWork<T> TryStart(Func<T> reconstruct)
        {
            // Invalidating publication cannot undo native constructor side
            // effects. Let an older constructor finish before starting another.
            lock(gate){
                if(last!=null&&!last.Completion.IsCompleted)return null;
                var next=new LoadingWork<T>(reconstruct);last=next;
                next.Completion.ContinueWith(_=>{
                    lock(gate){if(ReferenceEquals(last,next))last=null;}
                },System.Threading.CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                return next;
            }
        }
    }

    internal sealed class LoadingFade
    {
        readonly Func<IEnumerator> fadeIn,fadeOut;
        readonly Action<IEnumerator> schedule;
        readonly Func<int> count;
        readonly Action<Exception> log;
        bool begun,inDone,acquired,requested,outStarted,outDone,transferred;
        internal bool Ready { get { return inDone; } }
        internal bool Released { get { return !begun||(inDone&&!acquired)||outDone; } }
        internal Exception Error { get; private set; }
        internal Func<bool,IEnumerator> RecoverRelease;
        internal LoadingFade(Func<IEnumerator> fadeIn,Func<IEnumerator> fadeOut,
            Action<IEnumerator> schedule,Func<int> count,Action<Exception> log)
        {this.fadeIn=fadeIn;this.fadeOut=fadeOut;this.schedule=schedule;this.count=count;this.log=log;}
        internal void Begin(){if(!begun){begun=true;schedule(In());}}
        internal void Transfer(){transferred=true;}
        internal void Release(){requested=true;TryRelease();}
        void TryRelease(){if(requested&&inDone&&acquired&&!outStarted&&!transferred){outStarted=true;schedule(Out());}}
        IEnumerator In()
        {
            try{
                if(requested)yield break;
                int before=count();
                using(var sequence=new LoadingSequence(Create(fadeIn))){
                    bool more=sequence.MoveNext();
                    acquired=count()==before+1;
                    if(!acquired){Error=sequence.Error??new InvalidOperationException("Loading fade did not acquire its expected transition.");log(Error);yield break;}
                    while(more){yield return sequence.Current;more=sequence.MoveNext();}
                    if(sequence.Error!=null){Error=sequence.Error;log(Error);}
                }
            }finally{inDone=true;TryRelease();}
        }
        IEnumerator Out()
        {
            try{
                using(var sequence=new LoadingSequence(Create(fadeOut))){
                    int before=count();bool more=sequence.MoveNext();
                    bool leaseReleased=count()==before-1;
                    while(more){yield return sequence.Current;more=sequence.MoveNext();}
                    if(sequence.Error!=null){
                        Error=sequence.Error;log(Error);
                        if(RecoverRelease!=null){
                            using(var recovery=new LoadingSequence(RecoverRelease(leaseReleased))){
                                while(recovery.MoveNext())yield return recovery.Current;
                                if(recovery.Error!=null)log(recovery.Error);
                            }
                        }
                    }
                }
            }finally{outDone=true;}
        }
        static IEnumerator Create(Func<IEnumerator> factory){yield return factory();}
        internal static IEnumerator FinishWhenUnowned(IEnumerator finish,Func<int> count)
        {
            try{while(count()==0&&finish.MoveNext())yield return finish.Current;}
            finally{(finish as IDisposable)?.Dispose();}
        }
    }

    internal sealed class LoadingFlow
    {
        readonly Func<bool> owns,released;
        readonly Action restore,close,back,release;
        readonly Action<Action> show;
        readonly Action<Exception> log;
        bool valid=true,committed,restored,dialogShown,releaseRequested,dismissed;
        internal bool Alive { get { return valid&&owns(); } }
        internal LoadingFlow(Func<bool> owns,Action restore,Action<Action> show,Action close,
            Action back,Action<Exception> log,Action release,Func<bool> released)
        {this.owns=owns;this.restore=restore;this.show=show;this.close=close;this.back=back;this.log=log;this.release=release;this.released=released;}
        internal void Commit(){committed=true;}
        internal void UndoCommit(){committed=false;}
        void Release(){if(!committed&&!releaseRequested){releaseRequested=true;release();}}
        void Restore(){if(!restored){restored=true;restore();}}
        void Close(){if(dialogShown){dialogShown=false;close();}}
        internal void Cancel()
        {
            if(!valid)return;
            bool canRestore=owns()&&!committed;valid=false;
            try{Close();}finally{Release();if(canRestore)Restore();}
        }
        internal IEnumerator Run(IEnumerator original)
        {
            using(var sequence=new LoadingSequence(original)){
                try{
                    while(Alive&&sequence.MoveNext()){
                        if(!Alive)yield break;
                        yield return sequence.Current;
                    }
                    if(!Alive||committed)yield break;
                    var error=sequence.Error??new InvalidOperationException("Loading ended without a game.");
                    log(error);Release();
                    while(Alive&&!released())yield return null;
                    if(!Alive)yield break;
                    Restore();dialogShown=true;
                    show(()=>{if(Alive)dismissed=true;});
                    while(Alive&&!dismissed)yield return null;
                    if(!Alive)yield break;
                    Close();back();
                }finally{Cancel();}
            }
        }
    }

    // Workers return data only. Publication and UI belong to the calling thread.
    internal sealed class LoadingWork<T>
    {
        sealed class Outcome { internal T Value; internal Exception Error; }
        readonly Task<Outcome> completion;
        bool invalid,consumed;
        internal Task Completion { get { return completion; } }
        internal LoadingWork(Func<T> reconstruct)
        {
            completion=Task.Run(()=>{
                try{return new Outcome{Value=reconstruct()};}
                catch(Exception error){return new Outcome{Error=error};}
            });
        }
        internal void Invalidate(){invalid=true;}
        internal bool TryTake(out T value,out Exception error)
        {
            value=default(T);error=null;
            if(invalid||consumed||!completion.IsCompleted)return false;
            consumed=true;var outcome=completion.GetAwaiter().GetResult();
            value=outcome.Value;error=outcome.Error;return true;
        }
    }

    // Own nested compiler iterators so a child fault cannot strand its parent.
    // Engine wait instructions remain engine waits, including CustomYieldInstruction.
    internal sealed class LoadingSequence:IEnumerator,IDisposable
    {
        readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
        internal Exception Error { get; private set; }
        public object Current { get; private set; }
        internal LoadingSequence(IEnumerator root){stack.Push(root);}
        public bool MoveNext()
        {
            try{
                while(stack.Count>0){
                    var top=stack.Peek();
                    if(!top.MoveNext()){
                        stack.Pop();(top as IDisposable)?.Dispose();continue;
                    }
                    var child=top.Current as IEnumerator;
                    if(child!=null&&!(child is CustomYieldInstruction)){stack.Push(child);continue;}
                    Current=top.Current;return true;
                }
            }catch(Exception error){Error=error;Dispose();}
            return false;
        }
        public void Dispose()
        {
            while(stack.Count>0){
                try{(stack.Pop() as IDisposable)?.Dispose();}
                catch(Exception error){if(Error==null)Error=error;}
            }
        }
        public void Reset(){throw new NotSupportedException();}
    }
}
