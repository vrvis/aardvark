using System;
using System.Collections.Generic;
using System.Threading;

namespace Aardvark.Base
{
    internal class Disposable : IDisposable
    {
        private bool m_isDisposed = false;
        private readonly Action m_action;
        public Disposable(Action action)
            => m_action = action ?? throw new ArgumentNullException(nameof(action));

        public void Dispose()
        {
            if (m_isDisposed) return;

            lock (m_action)
            {
                if (m_isDisposed) return;
                m_isDisposed = true;
                m_action();
            }
        }
    }

    internal class Observer<T> : IObserver<T>
    {
        private readonly Action<T> m_onNext;
        private readonly Action<Exception> m_onError;
        private readonly Action m_onCompleted;
        public Observer(Action<T> onNext, Action<Exception> onError = null, Action onCompleted = null)
        {
            m_onNext = onNext;
            m_onError = onError;
            m_onCompleted = onCompleted;
        }

        public void OnNext(T value) => m_onNext?.Invoke(value);
        public void OnError(Exception error) => m_onError?.Invoke(error);
        public void OnCompleted() => m_onCompleted?.Invoke();
    }

    internal class Subject<T> : IObservable<T>, IObserver<T>
    {
        private readonly HashSet<IObserver<T>> m_observers = new HashSet<IObserver<T>>();

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            lock (m_observers) m_observers.Add(observer);
            return new Disposable(() =>
            {
                lock (m_observers) m_observers.Remove(observer);
            });
        }

        public void OnCompleted()
        {
            lock (m_observers)
            {
                foreach (var x in m_observers) x.OnCompleted();
            }
        }

        public void OnError(Exception error)
        {
            lock (m_observers)
            {
                foreach (var x in m_observers) x.OnError(error);
            }
        }

        public void OnNext(T value)
        {
            lock (m_observers)
            {
                foreach (var x in m_observers) x.OnNext(value);
            }
        }
    }

    internal static class Observable
    {
        private class NeverImpl<T> : IObservable<T>
        {
            public IDisposable Subscribe(IObserver<T> observer) => new Disposable(() => { });
        }

        public static IObservable<T> Never<T>() => new NeverImpl<T>();

        public static IDisposable Subscribe<T>(this IObservable<T> self,
            Action<T> onNext, Action<Exception> onError = null, Action onCompleted = null
            )
            => self.Subscribe(new Observer<T>(onNext, onError, onCompleted));
        
        public static IObservable<R> Select<T, R>(this IObservable<T> self, Func<T, R> map)
        {
            var subject = new Subject<R>();
            var dispose = default(IDisposable);
            void onNext(T x) => subject.OnNext(map(x));
            void onError(Exception e) { subject.OnError(e); dispose.Dispose(); }
            void onCompleted() { subject.OnCompleted(); dispose.Dispose(); }
            dispose = self.Subscribe(onNext, onError, onCompleted);
            return subject;
        }
    }
}
