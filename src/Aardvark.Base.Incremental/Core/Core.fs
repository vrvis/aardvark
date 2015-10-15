﻿namespace Aardvark.Base.Incremental

open System
open System.Collections.Generic
open Aardvark.Base
open System.Collections.Concurrent
open System.Threading

type VolatileCollection<'a when 'a : not struct>() =
    let set = HashSet<'a>()

    member x.IsEmpty = lock set (fun () -> set.Count = 0)

    member x.Consume() : seq<'a> =
        lock set (fun () -> 
            let arr = set |> Seq.toArray
            set.Clear()
            arr :> _
        )

    member x.Add(value : 'a) : bool =
        lock set (fun () -> set.Add value)

    member x.Remove(value : 'a) : bool =
        lock set (fun () -> set.Remove value)

    member x.Seq = set |> Seq.toList :> seq<'a>

/// <summary>
/// IAdaptiveObject represents the core interface for all
/// adaptive objects and contains everything necessary for
/// tracking OutOfDate flags and managing in-/outputs in the
/// dependency tree.
///
/// Since eager evalutation might be desirable in some scenarios
/// the interface also contains a Level representing the execution
/// order when evaluating inside a transaction and a function called
/// Mark allowing implementations to actually perform the evaluation.
/// Mark returns a bool since eager evaluation might cause the change
/// propagation process to exit early (if the actual value was unchanged)
/// In order to make adaptive objects easily identifiable all adaptive
/// objects must also provide a globally unique id (Id)
/// </summary>
[<AllowNullLiteral>]
type IAdaptiveObject =

    /// <summary>
    /// the globally unique id for the adaptive object
    /// </summary>
    abstract member Id : int

    /// <summary>
    /// the level for an adaptive object represents the
    /// maximal distance from an input cell in the depdency graph
    /// Note that this level is entirely managed by the system 
    /// and shall not be accessed directly by users of the system.
    /// </summary>
    abstract member Level : int with get, set

    /// <summary>
    /// Mark allows a specific implementation to
    /// evaluate the cell during the change propagation process.
    /// </summary>
    abstract member Mark : unit -> bool

    /// <summary>
    /// the outOfDate flag for the object is true
    /// whenever the object has been marked and shall
    /// be set to false by specific implementations.
    /// Note that this flag shall only be accessed when holding
    /// a lock on the adaptive object (allowing for concurrency)
    /// </summary>
    abstract member OutOfDate : bool with get, set

    /// <summary>
    /// the adaptive inputs for the object
    /// </summary>
    abstract member Inputs : ICollection<IAdaptiveObject>

    /// <summary>
    /// the adaptive outputs for the object which are recommended
    /// to be represented by Weak references in order to allow for
    /// unused parts of the graph to be garbage collected.
    /// </summary>
    abstract member Outputs : VolatileCollection<IAdaptiveObject>


    abstract member InputChanged : IAdaptiveObject -> unit
    

/// <summary>
/// LevelChangedException is internally used by the system
/// to handle level changes during the change propagation.
/// </summary>
exception LevelChangedException of changedObject : IAdaptiveObject * newLevel : int * distanceFromRoot : int

[<AutoOpen>]
module private AdaptiveSystemState =
    let currentEvaluationPath = new ThreadLocal<Stack<IAdaptiveObject>>(fun _ -> Stack())

type TrackAllThreadLocal<'a>(creator : unit -> 'a) =
    let mutable values : Map<int, 'a> = Map.empty

    let create() =
        let v = creator()
        Threading.Interlocked.Change(&values, Map.add Threading.Thread.CurrentThread.ManagedThreadId v) |> ignore
        v

    let inner = new Threading.ThreadLocal<'a>(create)

    member x.Value
        with get() = inner.Value
        and set v =
            inner.Value <- v
            Threading.Interlocked.Change(&values, Map.add Threading.Thread.CurrentThread.ManagedThreadId v) |> ignore

    member x.Values =
        values |> Map.toSeq |> Seq.map snd

    member x.Dispose() =
        values <- Map.empty
        inner.Dispose()

    interface IDisposable with
        member x.Dispose() = x.Dispose()


/// <summary>
/// Transaction holds a set of adaptive objects which
/// have been changed and shall therefore be marked as outOfDate.
/// Commit "propagates" these changes into the dependency-graph, takes
/// care of the correct execution-order and acquires appropriate locks
/// for all objects affected.
/// </summary>
type Transaction() =

    // each thread may have its own running transaction
    static let running = new TrackAllThreadLocal<Option<Transaction>>(fun () -> None)
    
    // we use a duplicate-queue here since we expect levels to be very similar 
    let q = DuplicatePriorityQueue<IAdaptiveObject, int>(fun o -> o.Level)
    let causes = Dict<IAdaptiveObject, HashSet<IAdaptiveObject>>()

    // the contained set is useful for determinig if an element has
    // already been enqueued
    let contained = HashSet<IAdaptiveObject>()
    let mutable current = None
    let mutable currentLevel = 0

    let getAndClear (set : ICollection<'a>) =
        let mutable content = []
        for e in set do content <- e::content
        set.Clear()
        content

    member x.IsContained e = contained.Contains e
    static member internal InAnyOfTheTransactionsInternal e =
        running.Values 
            |> Seq.toList 
            |> List.choose id 
            |> List.exists (fun t -> t.IsContained e)

    static member Running =
        running.Value

    static member HasRunning =
        running.Value.IsSome
       
    static member RunningLevel =
        match running.Value with
            | Some t -> t.CurrentLevel
            | _ -> Int32.MaxValue - 1


    member x.CurrentLevel = currentLevel

    /// <summary>
    /// enqueues an adaptive object for marking
    /// </summary>
    member x.Enqueue(e : IAdaptiveObject) =
        if contained.Add e then
            q.Enqueue e

    member x.Enqueue(e : IAdaptiveObject, cause : Option<IAdaptiveObject>) =
        if contained.Add e then
            q.Enqueue e
            match cause with
                | Some cause ->
                    match causes.TryGetValue e with
                        | (true, set) -> 
                            set.Add cause |> ignore
                        | _ ->
                            let set = HashSet [cause]
                            causes.[e] <- set
                | None -> ()

    member x.CurrentAdapiveObject = current
        

    /// <summary>
    /// performs the entire marking process causing
    /// all affected objects to be made consistent with
    /// the enqueued changes.
    /// </summary>
    member x.Commit() =
        // cache the currently running transaction (if any)
        // and make tourselves current.
        let old = running.Value
        running.Value <- Some x

        
        while q.Count > 0 do
            // dequeue the next element (having the minimal level)
            let l, e = q.Dequeue()
            currentLevel <- l
            current <- Some e

            

            let outputs = 
                // since we're about to access the outOfDate flag
                // for this object we must acquire a lock here.
                // Note that the transaction will at most hold one
                // lock at a time.
                lock e (fun () ->
                    // if the element is already outOfDate we
                    // do not traverse the graph further.
                    if e.OutOfDate then
                        Seq.empty

                    else
                        // if the object's level has changed since it
                        // was added to the queue we re-enqueue it with the new level
                        // Note that this may of course cause runtime overhead and
                        // might even change the asymptotic runtime behaviour of the entire
                        // system in the worst case but we opted for this approach since
                        // it is relatively simple to implement.
                        if l <> e.Level then
                            q.Enqueue e
                            Seq.empty
                        else
                            match causes.TryRemove e with
                                | (true, causes) -> causes |> Seq.iter e.InputChanged
                                | _ -> ()

                            // however if the level is consistent we may proceed
                            // by marking the object as outOfDate
                            e.OutOfDate <- true
                
                            try 
                                // here mark and the callbacks are allowed to evaluate
                                // the adaptive object but must expect any call to AddOutput to 
                                // raise a LevelChangedException whenever a level has been changed
                                if e.Mark() then
//                                    let mutable failed = false
//                                    let callbacks = e.MarkingCallbacks |> getAndClear
//                                    for cb in callbacks do 
//                                        try cb()
//                                        with :? LevelChangedException -> failed <- true
//                                    if failed then raise <| LevelChangedException e

                                    // if everything succeeded we return all current outputs
                                    // which will cause them to be enqueued 
                                    e.Outputs.Consume()

                                else
                                    // if Mark told us not to continue we're done here
                                    Seq.empty

                            with LevelChangedException(obj, objLevel, distance) ->
                                // if the level was changed either by a callback
                                // or Mark we re-enqueue the object with the new level and
                                // mark it upToDate again (since it would otherwise not be processed again)
                                e.Level <- max e.Level (objLevel + distance)
                                e.OutOfDate <- false
                                q.Enqueue e
                                Seq.empty
                )

            // finally we enqueue all returned outputs
            for o in outputs do
                o.InputChanged e
                x.Enqueue o

            contained.Remove e |> ignore
            current <- None
            

        // when the commit is over we restore the old
        // running transaction (if any)
        running.Value <- old
        currentLevel <- 0

/// <summary>
/// defines a base class for all adaptive objects implementing
/// IAdaptiveObject.
/// </summary>
type AdaptiveObject() =
    let id = newId()
    let mutable outOfDate = true
    let mutable level = 0
    let inputs = HashSet<IAdaptiveObject>() :> ICollection<_>
    let outputs = VolatileCollection<IAdaptiveObject>()

    static let time = AdaptiveObject() :> IAdaptiveObject
    
    let evaluate (this : IAdaptiveObject) (otherwise : Option<'a>) (f : unit -> 'a) =
        let stack = currentEvaluationPath.Value
        let top = stack.Count = 0 && not Transaction.HasRunning

        
        let res =
            lock this (fun () ->
                
                let value =
                    if outOfDate then None
                    else otherwise

                match value with
                    | Some v -> v
                    | None ->
                        let parent = if stack.Count > 0 then stack.Peek() |> Some else None
                        stack.Push this
                       
                        try
                            // this evaluation is performed optimistically
                            // meaning that the "top-level" object needs to be allowed to
                            // pull at least one value on every path.
                            // This property must therefore be maintained for every
                            // path in the entire system.
                            let r = f()

                            // if the object's level just got greater than or equal to
                            // the level of the running transaction (if any)
                            // we raise an exception since the evaluation
                            // could be inconsistent atm.
                            // the only exception to that is the top-level object itself
                            let maxAllowedLevel =
                                if stack.Count > 1 then Transaction.RunningLevel - 1
                                else Transaction.RunningLevel

                            if level > maxAllowedLevel then
                                // i am a pull from the future   
                                raise <| LevelChangedException(this, level, stack.Count - 1)


                            match parent with
                                | Some o when o.Inputs.Contains this ->
                                    outputs.Add o |> ignore
                                    o.Level <- max o.Level (level + 1)
                                | _ -> ()

                            outOfDate <- false
                            r
                        finally
                            stack.Pop() |> ignore
            )

        if top then 
            if not time.Outputs.IsEmpty then
                let t = Transaction()
                for o in time.Outputs.Consume() do
                    t.Enqueue(o)
                t.Commit()

        res

    static member Time = time



    /// <summary>
    /// utility function for evaluating an object if
    /// it is marked as outOfDate. If the object is actually
    /// outOfDate the given function is executed and otherwise
    /// the given default value is returned.
    /// Note that this function takes care of appropriate locking
    /// </summary>
    member x.EvaluateIfNeeded (otherwise : 'a) (f : unit -> 'a) =
        evaluate x (Some otherwise) f

    /// <summary>
    /// utility function for evaluating an object even if it
    /// is not marked as outOfDate.
    /// Note that this function takes care of appropriate locking
    /// </summary>
    member x.EvaluateAlways (f : unit -> 'a) =
        evaluate x None f


    member x.Id = id
    member x.OutOfDate
        with get() = outOfDate
        and set v = outOfDate <- v

    member x.Outputs = outputs
    member x.Inputs = inputs
    member x.Level 
        with get() = level
        and set l = level <- l

    abstract member Mark : unit -> bool
    default x.Mark () = true
    
    abstract member InputChanged : IAdaptiveObject -> unit
    default x.InputChanged ip = ()

    override x.GetHashCode() = id
    override x.Equals o =
        match o with
            | :? IAdaptiveObject as o -> id = o.Id
            | _ -> false

    interface IAdaptiveObject with
        member x.Id = id
        member x.OutOfDate
            with get() = outOfDate
            and set v = outOfDate <- v

        member x.Outputs = outputs
        member x.Inputs = inputs
        member x.Level 
            with get() = level
            and set l = level <- l

        member x.Mark () =
            x.Mark ()

        member x.InputChanged ip = x.InputChanged ip



/// <summary>
/// defines a base class for all decorated mods
/// </summary>
type AdaptiveDecorator(o : IAdaptiveObject) =
    let id = newId()
    
    member x.Id = id
    member x.OutOfDate
        with get() = o.OutOfDate
        and set v = o.OutOfDate <- v

    member x.Outputs = o.Outputs
    member x.Inputs = o.Inputs
    member x.Level 
        with get() = o.Level
        and set l = o.Level <- l

    member x.Mark() = o.Mark()

    override x.GetHashCode() = o.GetHashCode()
    override x.Equals o =
        match o with
            | :? IAdaptiveObject as o -> x.Id = o.Id
            | _ -> false

    interface IAdaptiveObject with
        member x.Id = id
        member x.OutOfDate
            with get() = o.OutOfDate
            and set v = o.OutOfDate <- v

        member x.Outputs = o.Outputs
        member x.Inputs = o.Inputs
        member x.Level 
            with get() = o.Level
            and set l = o.Level <- l

        member x.Mark () = o.Mark()

        member x.InputChanged ip = o.InputChanged ip

/// <summary>
/// defines a base class for all adaptive objects which are
/// actually constant.
/// Note that this class provides "dummy" implementations
/// for all memebers defined in IAdaptiveObject and does not 
/// keep track of in-/outputs.
/// </summary>
[<AbstractClass>]
type ConstantObject() =
    static let emptySet = EmptyCollection<IAdaptiveObject>() :> ICollection<_>
    interface IAdaptiveObject with
        member x.Id = -1
        member x.Level
            with get() = 0
            and set l = failwith "cannot set level for constant"

        member x.Mark() = false
        member x.OutOfDate
            with get() = false
            and set o = failwith "cannot mark constant outOfDate"

        member x.Inputs = emptySet
        member x.Outputs = VolatileCollection()
        member x.InputChanged ip = ()


and EmptyCollection<'a>() =
    interface ICollection<'a> with
        member x.Add _ = ()
        member x.Clear() = ()
        member x.Count = 0
        member x.Contains _ = false
        member x.Remove _ = false
        member x.CopyTo(arr, idx) = ()
        member x.IsReadOnly = false
        member x.GetEnumerator() : IEnumerator<'a> = Seq.empty.GetEnumerator()
        member x.GetEnumerator() : System.Collections.IEnumerator = Seq.empty.GetEnumerator() :> _



[<AutoOpen>]
module Marking =



    // changes the level of an adaptive object if it is below
    // minLevel and transitively changes all outputs if needed.
    // Note that this implementation has very poor runtime performance
    // and might possibly be improved using some kind of order-maintenance
    // structure instead of integers.
    let rec relabel (m : IAdaptiveObject) (minLevel : int) =
        let old = m.Level
        if old < minLevel then
            m.Level <- minLevel
            for o in m.Outputs.Seq do
                relabel o (minLevel + 1) |> ignore
            old <> -1
        else
            false

    // since changeable inputs need a transaction
    // for enqueing their changes we use a thread local 
    // current transaction which basically allows for 
    // an implicit argument.
    let private current = new Threading.ThreadLocal<Option<Transaction>>(fun () -> None)

    /// <summary>
    /// returns the currently running transaction or (if none)
    /// the current transaction for the calling thread
    /// </summary>
    let getCurrentTransaction() =
        match Transaction.Running with
            | Some r -> Some r
            | None ->
                match current.Value with
                    | Some c -> Some c
                    | None -> None

    let setCurrentTransaction t =
        current.Value <- t

    /// <summary>
    /// executes a function "inside" a newly created
    /// transaction and commits the transaction
    /// </summary>
    let transact (f : unit -> 'a) =
        let t = Transaction()
        let old = current.Value
        current.Value <- Some t
        let r = f()
        current.Value <- old
        t.Commit()
        r

    // defines some extension utilites for
    // IAdaptiveObjects
    type IAdaptiveObject with
        /// <summary>
        /// utility for marking adaptive object as outOfDate.
        /// Note that this function will actually enqueue the
        /// object to the current transaction and will fail if
        /// no current transaction can be found.
        /// However objects which are already outOfDate might
        /// also be "marked" when not having a current transaction.
        /// </summary>
        member x.MarkOutdated (cause : Option<IAdaptiveObject>) =
            match getCurrentTransaction() with
                | Some t -> t.Enqueue(x, cause)
                | None -> 
                    lock x (fun () -> 
                        if x.OutOfDate then ()
                        elif x.Outputs.IsEmpty then x.OutOfDate <- true
                        else failwith "cannot mark object without transaction"
                    )

        member x.MarkOutdated () =
            x.MarkOutdated None
                            
        /// <summary>
        /// utility for adding an output to the object.
        /// Note that this will cause the output to be marked
        /// using MarkOutdated and may therefore only be used
        /// on objects being outOfDate or inside a transaction.
        /// </summary>
        member x.AddOutput(m : IAdaptiveObject) =
            m.Inputs.Add x |> ignore
            x.Outputs.Add m |> ignore

            //m.Level <- max m.Level (x.Level + 1)

//            // if the element was actually relabeled and we're
//            // currently inside a running transaction we need to
//            // raise a LevelChangedException.
//            if relabel m (x.Level + 1) then
//                match Transaction.Running with
//                    | Some t ->
//                        match t.CurrentAdapiveObject with
//                            | Some m' when m = m' -> raise <| LevelChangedException m
//                            | _ -> ()
//                    | _ -> ()

            m.MarkOutdated ( Some x )

        /// <summary>
        /// utility for removing an output from the object
        /// </summary>
        member x.RemoveOutput (m : IAdaptiveObject) =
            m.Inputs.Remove x |> ignore
            x.Outputs.Remove m |> ignore


[<AutoOpen>]
module CallbackExtensions =
    
    type private CallbackObject(inner : IAdaptiveObject, callback : unit -> unit) as this =
        static let emptySet = EmptyCollection<IAdaptiveObject>() :> ICollection<_>

        let modId = newId()
        let mutable level = inner.Level + 1

        let inputs = HashSet<IAdaptiveObject>() :> ICollection<_>
        let mutable scope = Ag.getContext()
        let mutable inner = inner
        let mutable callback = fun () -> Ag.useScope scope callback
        do inner.AddOutput this


        interface IAdaptiveObject with
            member x.Id = modId
            member x.Level
                with get() = level
                and set l = level <- l

            member x.Mark() =
                callback()
                false

            member x.OutOfDate
                with get() = false
                and set o = ()

            member x.Inputs = inputs
            member x.Outputs = VolatileCollection()
            member x.InputChanged ip = ()

        interface IDisposable with
            member x.Dispose() =
                inner.RemoveOutput x
                callback <- id
                scope <- Unchecked.defaultof<_>
                inner <- null


    type IAdaptiveObject with

        /// <summary>
        /// utility for adding a "persistent" callback to
        /// the object. returns a disposable "subscription" which
        /// allows to destroy the callback.
        /// </summary>
        member x.AddMarkingCallback(f : unit -> unit) =
            let self = ref Unchecked.defaultof<_>
            self := 
                new CallbackObject(x, fun () ->
                    try
                        f()
                    finally 
                        x.Outputs.Add !self |> ignore
                )

            x.AddOutput !self

            !self :> IDisposable //{ new IDisposable with member __.Dispose() = live := false; x.MarkingCallbacks.Remove !self |> ignore}
 

open System.Threading
 

 
type VolatileDirtySet<'a, 'b when 'a :> IAdaptiveObject and 'a : equality and 'a : not struct>(eval : 'a -> 'b) =
    let mutable set : PersistentHashSet<'a> = PersistentHashSet.empty

    member x.Evaluate() =
        let local = Interlocked.Exchange(&set, PersistentHashSet.empty) 
        try
            local |> PersistentHashSet.toList
                    |> List.filter (fun o -> lock o (fun () -> o.OutOfDate))
                    |> List.map (fun o -> eval o)

        with :? LevelChangedException as l ->
            Interlocked.Change(&set, PersistentHashSet.union local) |> ignore
            raise l

    member x.Push(i : 'a) =
        lock i (fun () ->
            if i.OutOfDate then
                Interlocked.Change(&set, PersistentHashSet.add i) |> ignore
        )

    member x.Add(i : 'a) =
        x.Push(i)

    member x.Remove(i : 'a) =
        Interlocked.Change(&set, PersistentHashSet.remove i) |> ignore
 
    member x.Clear() =
        Interlocked.Exchange(&set, PersistentHashSet.empty) |> ignore

type VolatileTaggedDirtySet<'a, 'b, 't when 'a :> IAdaptiveObject and 'a : equality and 'a : not struct>(eval : 'a -> 'b) =
    let mutable set : PersistentHashSet<'a> = PersistentHashSet.empty
    let tagDict = Dictionary<'a, HashSet<'t>>()

    member x.Evaluate() =
        let local = Interlocked.Exchange(&set, PersistentHashSet.empty) 
        try
            local |> PersistentHashSet.toList
                  |> List.filter (fun o -> lock o (fun () -> o.OutOfDate))
                  |> List.map (fun o ->
                        match tagDict.TryGetValue o with
                            | (true, tags) -> o, Seq.toList tags
                            | _ -> o, []
                     )
                  |> List.map (fun (o, tags) -> eval o, tags)

        with :? LevelChangedException as l ->
            Interlocked.Change(&set, PersistentHashSet.union local) |> ignore
            raise l

    member x.Push(i : 'a) =
        lock i (fun () ->
            if i.OutOfDate then
                Interlocked.Change(&set, PersistentHashSet.add i) |> ignore
        )

    member x.Add(tag : 't, i : 'a) =
        match tagDict.TryGetValue i with
            | (true, set) -> 
                set.Add tag |> ignore
                false
            | _ ->
                tagDict.[i] <- HashSet [tag]
                x.Push i
                true

    member x.Remove(tag : 't, i : 'a) =
        match tagDict.TryGetValue i with
            | (true, tags) -> 
                if tags.Remove tag then
                    if tags.Count = 0 then
                        Interlocked.Change(&set, PersistentHashSet.remove i) |> ignore
                        true
                    else
                        false
                else
                    failwithf "[VolatileTaggedDirtySet] could not remove tag %A for element %A" tag i
                                      
            | _ ->
                failwithf "[VolatileTaggedDirtySet] could not remove element: %A" i

    member x.Clear() =
        tagDict.Clear()
        Interlocked.Exchange(&set, PersistentHashSet.empty) |> ignore


