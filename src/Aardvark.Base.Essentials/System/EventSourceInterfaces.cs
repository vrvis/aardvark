using System;

namespace Aardvark.Base
{
    public class Unit
    {
        public static Unit Default = new Unit();

        private Unit() { }
    }

    /// <summary>
    /// The receiving side of an event source.
    /// </summary>
    public interface IEvent<T> : IEvent
    {
        /// <summary>
        /// The latest value that has been emitted.
        /// This will return default(T) if no value has been emitted yet. 
        /// </summary>
        T Latest { get; }

        /// <summary>
        /// The next value that will be emitted.
        /// </summary>
        new IAwaitable<T> Next { get; }

        /// <summary>
        /// Observable sequence of emitted values.
        /// </summary>
        new IObservable<T> Values { get; }
    }

    /// <summary>
    /// The sending side of an event source.
    /// </summary>
    public interface IEventEmitter<T> : IEventEmitter
    {
        /// <summary>
        /// Pushes next event value.
        /// </summary>
        void Emit(T value);
    }

    /// <summary>
    /// The receiving side of an event source.
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// The next value that will be emitted.
        /// </summary>
        IAwaitable Next { get; }

        /// <summary>
        /// Observable notifications for all values that are emitted.
        /// </summary>
        IObservable<Unit> Values { get; }
    }

    /// <summary>
    /// The sending side of an event source.
    /// </summary>
    public interface IEventEmitter
    {
        /// <summary>
        /// Pushes next event.
        /// </summary>
        void Emit();
    }
}
