#if INTERACTIVE
#r "..\\..\\Packages\\NUnit.2.6.3\\lib\\nunit.framework.dll"
#r "..\\..\\Bin\\Release\\Aardvark.Base.dll"
#r "..\\..\\Bin\\Release\\Aardvark.Preliminary.dll"
#r "..\\..\\Bin\\Release\\Aardvark.Base.FSharp.dll"
open Aardvark.Base
#else
namespace Aardvark.Base
#endif

#nowarn "44"

open System

/// Time represents a order-maintenance structure
/// providing operations "after" and "delete"
/// maintaining O(1) comparisons between times
[<Obsolete>]
[<AllowNullLiteral>]
type Time =
    class
        /// The representant-time for this tims
        val mutable public Representant  : Time

        /// The total number of times currently in the time-list
        /// NOTE that Count is only valid for the representant-node
        val mutable public Count : int


        val mutable internal m_time : uint64
        val mutable internal NextArray : array<TimeLink>
        val mutable internal PrevArray : array<TimeLink>

        /// gets the direct successor for the time
        member x.Next = x.NextArray.[0].Target

        /// gets the direct predecessor for the time
        member x.Prev = x.PrevArray.[0].Target

        /// gets the height of the skip-node
        member internal x.Height = 
            assert (x.NextArray.Length = x.PrevArray.Length)
            x.NextArray.Length

        /// gets the comparable key for the time (as uint64)
        member x.Time =
            x.m_time - x.Representant.m_time

        /// gets the n-th time after this one
        /// NOTE that this only works on representant-nodes
        member x.TryAt (index : int) =
            if x <> x.Representant then
                failwith "indexing operations can only be performed on the representant-time"

            let rec search (index : int) (level : int) (t : Time) : Option<Time> =
                if level < 0 then
                    if index = 0 then Some t
                    else None
                else
                    let link = t.NextArray.[level]
                    if index < link.Width then
                        search index (level - 1) t
                    else
                        search (index - link.Width) level link.Target

            if index >= 0 && index < x.Count then
                search index (x.Height - 1) x
            else
                None

        member x.Item
            with get (index : int) = 
                match x.TryAt(index) with
                    | Some t -> t
                    | None -> raise <| IndexOutOfRangeException()

        interface IComparable with
            member x.CompareTo o =
                match o with
                    | :? Time as o -> 
                        assert (o.Representant = x.Representant)
                        compare x.Time o.Time
                    | _ -> failwithf "cannot compare time to %A" o

        override x.GetHashCode() = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x)
        override x.Equals o = System.Object.ReferenceEquals(x,o)
        override x.ToString() = sprintf "%.5f" ((float x.Time) / (float System.UInt64.MaxValue))
        internal new (h) = { Representant = null; m_time = 0UL; NextArray = Array.zeroCreate h; PrevArray = Array.zeroCreate h; Count = 1 }

    end


and TimeLink =
    struct
        val mutable public Width : int
        val mutable public Target : Time

        new(w,t) = { Width = w; Target = t }
    end

[<Obsolete>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Time =
    let private random = Random()
    let private randomHeight() =
        let mutable h = 1
        while random.NextDouble() < 0.5 do
            h <- h + 1
        h

    let inline private (|TimeLink|) (t : TimeLink) =
        TimeLink(t.Width, t.Target)

    let private printLevels (t : Time) =
        assert (t = t.Representant)
        for l in 0..t.Height-1 do
            let (TimeLink(d,n)) = t.NextArray.[l]
            printf "%d: %A" l t
            printf " -(%d)-> %A " d n
            let mutable current = t.NextArray.[l].Target
            while current <> t do
                let (TimeLink(d,n)) = current.NextArray.[l]
                printf " -(%d)-> %A  " d n
                current <- n
            printfn ""


    /// creates a new root-time
    let newRoot() =
        let h = randomHeight()
        let root = Time(1)

        root.Representant <- root
        root.NextArray.[0] <- TimeLink(1,root)
        root.PrevArray.[0] <- TimeLink(1,root)

        root

    /// computes the (unsigned) distance between two times.
    /// NOTE that "distance a a" is UInt64.MaxValue
    let private distance (t0 : Time) (t1 : Time) =
        if t0 = t1 then System.UInt64.MaxValue
        else t1.Time - t0.Time

    /// creates a new time directly after the given one
    /// in runtime O(log N) time.
    let after (t : Time) =
        let mutable dn = distance t t.NextArray.[0].Target

        // if the distance to the next time is 1 (no room)
        // relabel all times s.t. the new one can be inserted
        if dn = 1UL then
            // find a range s.t. distance(range) >= 1 + |range|^2 
            let mutable current = t.NextArray.[0].Target
            let mutable j = 1UL
            while distance t current < 1UL + j * j do
                current <- current.NextArray.[0].Target
                j <- j + 1UL

            // distribute all times in the range equally spaced
            let step = (distance t current) / j
            current <- t.NextArray.[0].Target
            let mutable currentTime = t.m_time + step
            for k in 1UL..(j-1UL) do
                current.m_time <- currentTime
                current <- current.NextArray.[0].Target
                currentTime <- currentTime + step

            // store the distance to the next time
            dn <- step

        let back (n : Time) =
            n.PrevArray.[n.PrevArray.Length - 1]
        
        // insert the new time with distance (dn / 2) after
        // the given one (there has to be enough room now)
        let h = randomHeight()
        let res = Time h
        res.m_time <- t.m_time + dn / 2UL
        res.Representant <- t.Representant


        // since the predecessor might be "smaller" than 
        // the new node we need to search the remaining links
        // by going backward in the list
        let mutable current = t
        let mutable distance = 1

        for i in 0..h-1 do
            let resize (n : Time) (h : int) =
                assert (n.Representant = n)
                if h > n.Height then
                    let additional = Array.create (h - n.Height) (TimeLink(n.Count,n))
                    n.NextArray <- Array.append n.NextArray additional
                    n.PrevArray <- Array.append n.PrevArray additional

            // go backwards until a node with sufficient height is found
            // or until we've reached the representant
            while i >= current.Height && current <> t.Representant do
                let (TimeLink(d,l)) = back current
                current <- l
                distance <- distance + d

            // if the found node is not sufficiently high it must
            // be the representant and therefore it has to be resized
            if i >= current.Height then
                resize current h

            // current must now be sufficiently high  
            let (TimeLink(d,n)) = current.NextArray.[i]
            current.NextArray.[i] <- TimeLink(distance, res)
            res.PrevArray.[i] <- TimeLink(distance, current)
            res.NextArray.[i] <- TimeLink(1 + d - distance, n)
            n.PrevArray.[i] <- TimeLink(1 + d - distance, res)

        // since the predecessor and the new node might be
        // smaller than the total height we need to increment
        // the width of all pointers "passing" the new node (above)
        let mutable current = t
        for i in h..t.Representant.Height-1 do
            while i >= current.Height && current <> t.Representant do
                let (TimeLink(d,l)) = back current
                current <- l

            let (TimeLink(d,n)) = current.NextArray.[i]
            current.NextArray.[i] <- TimeLink(d + 1, n)
            n.PrevArray.[i] <- TimeLink(d + 1, current)

        // finally increment the count and return the node
        t.Representant.Count <- t.Representant.Count + 1
        res

    /// creates a new time directly before the given one
    /// in runtime O(log N) time.
    let before (t : Time) =
        if t.Representant = t then failwith "cannot insert before root time"
        after (t.PrevArray.[0].Target)

    /// deletes a time in O(log N) runtime.
    let delete (t : Time) =
        for l in 0..t.Height-1 do
            let (TimeLink(dp, p)) = t.PrevArray.[l]
            let (TimeLink(dn, n)) = t.NextArray.[l]

            n.PrevArray.[l] <- TimeLink(dp + dn - 1, p)
            p.NextArray.[l] <- TimeLink(dp + dn - 1, n)

        let mutable current = t
        let mutable distance = 1
        for i in t.Height..t.Representant.Height-1 do
            let back (n : Time) =
                n.PrevArray.[n.PrevArray.Length - 1]

            // go backwards until a node with sufficient height is found
            // or until we've reached the representant
            while i >= current.Height && current <> t.Representant do
                let (TimeLink(d,l)) = back current
                current <- l
                distance <- distance + d

            let (TimeLink(dn, n)) = current.NextArray.[i]

            current.NextArray.[i] <- TimeLink(dn - 1, n)
            n.PrevArray.[i] <- TimeLink(dn - 1, current)

        // every level (except for 0) on which rep.NextArray.[level] = rep 
        // is useless and is therefore removed
        let rep = t.Representant
        let mutable repHeight = rep.Height
        while repHeight > 1 && rep.NextArray.[repHeight - 1].Target = rep do
            repHeight <- repHeight - 1

        if repHeight < rep.Height then
            rep.NextArray <- Array.sub rep.NextArray 0 repHeight
            rep.PrevArray <- Array.sub rep.PrevArray 0 repHeight
        t.NextArray <- null
        t.PrevArray <- null
        t.m_time <- 0UL
        t.Representant.Count <- t.Representant.Count - 1

    /// deletes all times associated with the given one
    /// in O(1) runtime.
    /// NOTE that the given time must be a representant-node
    let deleteAll (t : Time) =
        if t = t.Representant then
            let h = randomHeight()
            t.NextArray <- Array.create 1 (TimeLink(1,t))
            t.PrevArray <- Array.create 1 (TimeLink(1,t))
            t.Count <- 1
            t.m_time <- 0UL
        else
            failwith "deleteAll shall only be called on root-times"

    /// gets the nth element after the given time in O(log N) runtime.
    /// NOTE that the given time must be a representant-node
    let nth (index : int) (t : Time) =
        t.TryAt index

