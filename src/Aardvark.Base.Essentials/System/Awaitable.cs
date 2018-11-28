using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aardvark.Base
{
    /// <summary>
    /// Represents an awaitable with clear-push semantics. 
    /// Since Task/TaskCompletionSource are likely to spawn new threads 
    /// and execute the respective continuations on haphazardous threads 
    /// we implemeted our own awaitable executing all continuations on 
    /// the thread which they were triggered on.
    /// </summary>
    public class Awaitable : IAwaitable, IEventEmitter<Unit>
    {
        private int m_isCompleted;
        private List<Action> m_continuations;
        private SpinLock m_continuationLock;

        private CancellationToken? m_ct;
        private CancellationTokenRegistration m_ctRegistration;

        private Awaiter m_awaiter;

        #region Constructors

        /// <summary>
        /// </summary>
        public Awaitable(CancellationToken? ct = null)
        {
            m_isCompleted = 0;
            m_continuations = new List<Action>();
            m_continuationLock = new SpinLock();

            m_ct = ct;
            if (m_ct.HasValue)
            {
                if (m_ct.Value.CanBeCanceled)
                {
                    m_ctRegistration = m_ct.Value.Register(Emit);
                }
            }

            m_awaiter = new Awaiter(this);
        }

        #endregion

        #region Properties

        /// <summary>
        /// </summary>
        public bool IsCompleted
        {
            get { return m_isCompleted == 1; }
        }

        #endregion

        #region Methods

        /// <summary>
        /// </summary>
        public void Emit()
        {
            var old = Interlocked.Exchange(ref m_isCompleted, 1);
            if (old == 0)
            {
                //If the Awaitable has a CancellationToken release the registration
                if (m_ct.HasValue) m_ctRegistration.Dispose();

                List<Action> actions = null;
                bool taken = false;
                try
                {
                    m_continuationLock.Enter(ref taken);
                    actions = m_continuations;
                    m_continuations = null;
                }
                finally
                {
                    if (taken) m_continuationLock.Exit();
                }

                if (actions != null)
                {
                    var acts = Interlocked.Exchange(ref actions, new List<Action>());
                    foreach (var c in acts)
                    {
                        try { c(); }
                        catch { }
                    }
                }
            }
        }
        
        /// <summary>
        /// </summary>
        public void Emit(Unit value)
        {
            Emit();
        }

        /// <summary>
        /// </summary>
        public void Subscribe(Action continuation)
        {
            bool execute = false;
            bool taken = false;
            try
            {
                m_continuationLock.Enter(ref taken);

                if (m_isCompleted == 1)
                    execute = true;
                else
                    m_continuations.Add(continuation);
            }
            finally
            {
                if (taken) m_continuationLock.Exit();
            }

            if (execute)
                continuation();
        }

        #endregion

        #region IAwaitable Members

        /// <summary>
        /// </summary>
        public IAwaiter GetAwaiter()
        {
            return m_awaiter;
        }

        #endregion

        private struct Awaiter : IAwaiter
        {
            private Awaitable m_source;

            #region Constructors

            public Awaiter(Awaitable source)
            {
                m_source = source;
            }

            #endregion

            #region IAwaiter Members

            public bool IsCompleted
            {
                get { return m_source.m_isCompleted == 1; }
            }

            public void GetResult()
            {
                //since we're here in the waiting code again the exception will occur where expected
                if (m_source.m_ct.HasValue && m_source.m_ct.Value.IsCancellationRequested)
                    throw new TaskCanceledException();
            }

            public void OnCompleted(Action continuation)
            {
                m_source.Subscribe(continuation);
            }

            #endregion
        }
    }

    /// <summary>
    /// Represents an awaitable with clear-push semantics. 
    /// Since Task/TaskCompletionSource are likely to spawn new threads 
    /// and execute the respective continuations on haphazardous threads 
    /// we implemeted our own awaitable executing all continuations on 
    /// the thread which they were triggered on.
    /// </summary>
    public class Awaitable<T> : IAwaitable<T>, IEventEmitter<T>
    {
        private int m_isCompleted;
        private T m_result;

        private List<Action> m_continuations;
        private SpinLock m_continuationLock;

        private CancellationToken? m_ct;
        private CancellationTokenRegistration m_ctRegistration;

        private Awaiter m_awaiter;
        private ManualResetEventSlim m_onPush = null;

        #region Constructors

        /// <summary>
        /// </summary>
        public Awaitable(CancellationToken? ct = null)
        {
            m_isCompleted = 0;
            m_result = default(T);

            m_continuations = new List<Action>();
            m_continuationLock = new SpinLock();

            m_ct = ct;
            if (m_ct.HasValue)
            {
                if (m_ct.Value.CanBeCanceled)
                {
                    m_ctRegistration = m_ct.Value.Register(() => Emit(default(T)));
                }
            }

            m_awaiter = new Awaiter(this);
        }

        #endregion

        #region Properties

        /// <summary>
        /// </summary>
        public bool IsCompleted
        {
            get { return m_isCompleted == 1; }
        }

        /// <summary>
        /// </summary>
        public T Result
        {
            get
            {
                if (m_isCompleted == 1) return m_result;
                else
                {
                    if (m_onPush == null)
                    {
                        Interlocked.CompareExchange(ref m_onPush, new ManualResetEventSlim(), null);
                    }

                    m_onPush.Wait();

                    return m_result;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// </summary>
        public void Emit(T value)
        {
            var old = Interlocked.Exchange(ref m_isCompleted, 1);
            if (old == 0)
            {
                m_result = value;
                //If the Awaitable has a CancellationToken release the registration
                if (m_ct.HasValue) m_ctRegistration.Dispose();

                List<Action> actions = null;
                bool taken = false;
                try
                {
                    m_continuationLock.Enter(ref taken);
                    actions = m_continuations;
                    m_continuations = null;
                }
                finally
                {
                    if (taken) m_continuationLock.Exit();
                }

                if (actions != null)
                {
                    //Run all registered Continuations
                    var acts = Interlocked.Exchange(ref actions, new List<Action>());
                    foreach (var c in acts)
                    {
                        try { c(); }
                        catch { }
                    }
                }

                if (m_onPush != null)
                {
                    m_onPush.Set();
                    m_onPush = null;
                }

            }
        }

        /// <summary>
        /// </summary>
        public void Subscribe(Action continuation)
        {
            bool execute = false;
            bool taken = false;
            try
            {
                m_continuationLock.Enter(ref taken);

                if (m_isCompleted == 1)
                    execute = true;
                else
                    m_continuations.Add(continuation);
            }
            finally
            {
                if (taken) m_continuationLock.Exit();
            }

            if (execute)
                continuation();
        }

        /// <summary>
        /// </summary>
        public void Subscribe(Action<T> continuation)
        {
            if (m_isCompleted == 1)
            {
                Report.Warn("awaiting already completed Awaitable");
                continuation(m_result);
            }
            else
            {
                bool taken = false;
                try
                {
                    m_continuationLock.Enter(ref taken);
                    m_continuations.Add(() => continuation(m_result));
                }
                finally
                {
                    if (taken) m_continuationLock.Exit();
                }
            }
        }

        #endregion

        #region IAwaitable Members

        /// <summary>
        /// </summary>
        public IAwaiter<T> GetAwaiter()
        {
            return m_awaiter;
        }

        IAwaiter IAwaitable.GetAwaiter()
        {
            return m_awaiter;
        }

        #endregion

        #region IEventEmitter

        /// <summary>
        /// </summary>
        public void Emit()
        {
            Emit(default(T));
        }

        #endregion

        private struct Awaiter : IAwaiter<T>
        {
            private Awaitable<T> m_source;

            #region Constructors

            public Awaiter(Awaitable<T> source)
            {
                m_source = source;
            }

            #endregion

            #region IAwaiter Members

            public bool IsCompleted
            {
                get { return m_source.m_isCompleted == 1; }
            }

            public T GetResult()
            {
                //since we're here in the waiting code again the exception will occur where expected
                if (m_source.m_ct.HasValue && m_source.m_ct.Value.IsCancellationRequested) 
                    throw new TaskCanceledException();

                return m_source.m_result;
            }

            public void OnCompleted(Action continuation)
            {
                m_source.Subscribe(continuation);
            }

            void IAwaiter.GetResult()
            {
                //since we're here in the waiting code again the exception will occur where expected
                if (m_source.m_ct.HasValue) m_source.m_ct.Value.ThrowIfCancellationRequested();
            }

            #endregion
        }
    }

    /// <summary>
    /// Contains various combinators for Awaitables (e.g. WhenAny, WhenAll, WithCancellation) since the Task-combinators cannot be used for Awaitables.
    /// </summary>
    public static class Await
    {
        /// <summary>
        /// </summary>
        public static readonly Clock GlobalClock = new Clock(120);

        #region WithCancellation

        /// <summary>
        /// Creates a task that completes (or cancels) when either the input task completes or the cancellation token is signalled.
        /// On cancellation, the original task still runs to completion because there is no way to preemptively cancel it.
        /// </summary>
        public static IAwaitable<T> WithCancellation<T>(this IAwaitable<T> input, CancellationToken ct)
        {
            var result = new Awaitable<T>(ct);
            input.Subscribe(v => result.Emit(v));
            return result;
        }

        /// <summary>
        /// Creates a task that completes (or cancels) when either the input task completes or the cancellation token is signalled.
        /// On cancellation, the original task still runs to completion because there is no way to preemptively cancel it.
        /// </summary>
        public static IAwaitable WithCancellation(this IAwaitable input, CancellationToken ct)
        {
            var result = new Awaitable(ct);
            input.Subscribe(() => result.Emit());
            return result;
        }

        #endregion

        #region WhenAny

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        public static IAwaitable<IAwaitable> WhenAny(params IAwaitable[] inputs)
        {
            var result = new Awaitable<IAwaitable>();

            foreach (var i in inputs)
            {
                i.Subscribe(() =>
                {
                    if (!result.IsCompleted) result.Emit(i);
                });
            }

            return result;
        }
        
        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        public static IAwaitable<IAwaitable<T>> WhenAny<T>(params IAwaitable<T>[] inputs)
        {
            var result = new Awaitable<IAwaitable<T>>();

            foreach (var i in inputs)
            {
                i.Subscribe(v =>
                {
                    if (!result.IsCompleted) result.Emit(i);
                });
            }

            return result;
        }

        #endregion

        #region WhenAll

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        public static IAwaitable<T[]> WhenAll<T>(params IAwaitable<T>[] inputs)
        {
            var result = new Awaitable<T[]>();

            var set = new HashSet<IAwaitable<T>>(inputs);
            var output = new T[inputs.Length];
            var i = 0;
            foreach (var input in set)
            {
                var index = i;
                var ip = input;
                ip.Subscribe(v =>
                {
                    output[index] = v;
                    if (set.Remove(ip) && set.Count == 0)
                    {
                        if (!result.IsCompleted) result.Emit(output);
                    }
                });

                i++;
            }

            return result;
        }

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        public static IAwaitable WhenAll(params IAwaitable[] inputs)
        {
            var result = new Awaitable();

            var set = new HashSet<IAwaitable>(inputs);
            foreach (var input in set)
            {
                var ip = input;
                ip.Subscribe(() =>
                {
                    if (set.Remove(ip) && set.Count == 0)
                    {
                        if (!result.IsCompleted) result.Emit();
                    }
                });
            }

            return result;
        }

        #endregion

        #region Select

        /// <summary>
        /// Projects the result of the task into a new form.
        /// </summary>
        public static IAwaitable<TResult> Select<TInput, TResult>(this IAwaitable<TInput> input, Func<TInput, TResult> f)
        {
            var result = new Awaitable<TResult>();
            input.Subscribe(v => result.Emit(f(v)));
            return result;
        }

        /// <summary>
        /// Projects the result of the task into a new form.
        /// </summary>
        public static IAwaitable<TResult> Select<TResult>(this IAwaitable input, Func<TResult> f)
        {
            var result = new Awaitable<TResult>();
            input.Subscribe(() => result.Emit(f()));
            return result;
        }

        #endregion

        #region ContinueWith

        /// <summary>
        /// Creates a continuation that executes when the target task completes.
        /// </summary>
        public static IAwaitable<TResult> ContinueWith<T, TResult>(this IAwaitable<T> awaitable, Func<T, TResult> fun)
        {
            var result = new Awaitable<TResult>();

            awaitable.Subscribe(v =>
                {
                    result.Emit(fun(v));
                });

            return result;
        }

        /// <summary>
        /// Creates a continuation that executes when the target task completes.
        /// </summary>
        public static IAwaitable ContinueWith<T>(this IAwaitable<T> awaitable, Action<T> action)
        {
            var result = new Awaitable();

            awaitable.Subscribe(v =>
            {
                action(v);
                result.Emit();
            });

            return result;
        }

        /// <summary>
        /// Creates a continuation that executes when the target task completes.
        /// </summary>
        public static IAwaitable<TResult> ContinueWith<TResult>(this IAwaitable awaitable, Func<TResult> fun)
        {
            var result = new Awaitable<TResult>();

            awaitable.Subscribe(() =>
            {
                result.Emit(fun());
            });

            return result;
        }

        /// <summary>
        /// Creates a continuation that executes when the target task completes.
        /// </summary>
        public static IAwaitable ContinueWith<TResult>(this IAwaitable awaitable, Action fun)
        {
            var result = new Awaitable();

            awaitable.Subscribe(() =>
            {
                fun();
                result.Emit();
            });

            return result;
        }

        #endregion

        #region Delay

        /// <summary>
        /// Creates a task that will complete after a time delay.
        /// </summary>
        public static IAwaitable<TimeValue> Delay(uint milliseconds, CancellationToken ct)
        {
            return GlobalClock.Future((int)milliseconds).WithCancellation(ct);
        }

        /// <summary>
        /// Creates a task that will complete after a time delay.
        /// </summary>
        public static IAwaitable<TimeValue> Delay(uint milliseconds)
        {
            return GlobalClock.Future((int)milliseconds);
        }

        /// <summary>
        /// Creates a task that will complete after a time delay.
        /// </summary>
        public static IAwaitable<TimeValue> Delay(TimeSpan delay, CancellationToken ct)
        {
            return Delay((uint)delay.TotalMilliseconds, ct);
        }

        /// <summary>
        /// Creates a task that will complete after a time delay.
        /// </summary>
        public static IAwaitable<TimeValue> Delay(TimeSpan delay)
        {
            return Delay((uint)delay.TotalMilliseconds);
        }

        /// <summary>
        /// Creates a task that will complete after the shortest possible time delay.
        /// </summary>
        public static IAwaitable<TimeValue> Tick
        {
            get { return GlobalClock.Tick(); }
        }

        #endregion
    }

    /// <summary>
    /// </summary>
    public static class AwaitableTest
    {
        static async void Run(Awaitable<int> a, Awaitable<int> b, CancellationToken ct)
        {
            try
            {
                var i = await Await.WhenAny(a, b).WithCancellation(ct);
                Console.WriteLine("got: {0}", i);
            }
            catch (OperationCanceledException)
            {
                Report.Line("cancelled");
            }
        }

        /// <summary>
        /// </summary>
        public static void Run()
        {
            var c = new CancellationTokenSource();
            var ct = c.Token;

            var a = new Awaitable<int>();
            var b = new Awaitable<int>();
            Run(a, b, ct);

            //c.Cancel();
            a.Emit(1);
            b.Emit(2);

            Console.ReadLine();


        }
    }

    /// <summary>
    /// </summary>
    public class TaskAwaiter<T> : IAwaiter<T>
    {
        private System.Runtime.CompilerServices.TaskAwaiter<T> m_awaiter;

        /// <summary>
        /// </summary>
        public TaskAwaiter(System.Runtime.CompilerServices.TaskAwaiter<T> awaiter)
        {
            m_awaiter = awaiter;
        }

        /// <summary>
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            m_awaiter.OnCompleted(continuation);
        }

        /// <summary>
        /// </summary>
        public bool IsCompleted
        {
            get { return m_awaiter.IsCompleted; }
        }

        /// <summary>
        /// </summary>
        public T GetResult()
        {
            return m_awaiter.GetResult();
        }

        /// <summary>
        /// </summary>
        void IAwaiter.GetResult()
        {
            m_awaiter.GetResult();
        }
    }

    /// <summary>
    /// </summary>
    public class TaskAwaiter : IAwaiter
    {
        private System.Runtime.CompilerServices.TaskAwaiter m_awaiter;

        /// <summary>
        /// </summary>
        public TaskAwaiter(System.Runtime.CompilerServices.TaskAwaiter awaiter)
        {
            m_awaiter = awaiter;
        }

        /// <summary>
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            m_awaiter.OnCompleted(continuation);
        }

        /// <summary>
        /// </summary>
        public bool IsCompleted
        {
            get { return m_awaiter.IsCompleted; }
        }

        void IAwaiter.GetResult()
        {
            m_awaiter.GetResult();
        }
    }

    /// <summary>
    /// </summary>
    public class TaskAwaitable<T> : IAwaitable<T>
    {
        private Task<T> m_task;

        /// <summary>
        /// </summary>
        public TaskAwaitable(Task<T> task) { m_task = task; }

        /// <summary>
        /// </summary>
        public IAwaiter<T> GetAwaiter()
        {
            return new TaskAwaiter<T>(m_task.GetAwaiter());
        }

        /// <summary>
        /// </summary>
        public T Result
        {
            get { return m_task.Result; }
        }

        /// <summary>
        /// </summary>
        IAwaiter IAwaitable.GetAwaiter()
        {
            return new TaskAwaiter<T>(m_task.GetAwaiter());
        }

        /// <summary>
        /// </summary>
        public bool IsCompleted
        {
            get { return m_task.IsCompleted; }
        }
    }

    /// <summary>
    /// </summary>
    public class TaskAwaitable : IAwaitable
    {
        private Task m_task;

        /// <summary>
        /// </summary>
        public TaskAwaitable(Task task) { m_task = task; }

        /// <summary>
        /// </summary>
        public IAwaiter GetAwaiter()
        {
            return new TaskAwaiter(m_task.GetAwaiter());
        }

        bool IAwaitable.IsCompleted
        {
            get { return m_task.IsCompleted; }
        }
    }

    /// <summary>
    /// </summary>
    public static class TaskAwaitableExtensions
    {
        /// <summary>
        /// </summary>
        public static TaskAwaitable AsAwaitable(this Task task)
        { 
            return new TaskAwaitable(task); 
        }

        /// <summary>
        /// </summary>
        public static TaskAwaitable<T> AsAwaitable<T>(this Task<T> task)
        {
            return new TaskAwaitable<T>(task);
        }
    }
}
