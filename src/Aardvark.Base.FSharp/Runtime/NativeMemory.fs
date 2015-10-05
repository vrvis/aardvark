﻿namespace Aardvark.Base

open System
open System.Collections.Generic
open System.Threading
open System.Runtime.InteropServices
open Aardvark.Base

type FreeList<'k, 'v when 'k : comparison>() =
    static let comparer = { new IComparer<'k * HashSet<'v>> with member x.Compare((l,_), (r,_)) = compare l r }
    let sortedSet = SortedSetExt comparer
    let sets = Dictionary<'k, HashSet<'v>>()

    let tryGet (minimal : 'k) =
        let _, self, right = sortedSet.FindNeighbours((minimal, Unchecked.defaultof<_>))
    
        let fitting =
            if self.HasValue then Some self.Value
            elif right.HasValue then Some right.Value
            else None
        
        match fitting with
            | Some (k,container) -> 

                if container.Count <= 0 then
                    raise <| ArgumentException "invalid memory manager state"

                let any = container |> Seq.head
                container.Remove any |> ignore

                // if the container just got empty we remove it from the
                // sorted set and the cache-dictionary
                if container.Count = 0 then
                   sortedSet.Remove(k, container) |> ignore
                   sets.Remove(k) |> ignore

                Some any

            | None -> None

    let insert (k : 'k) (v : 'v) =
        match sets.TryGetValue k with
            | (true, container) ->
                container.Add(v) |> ignore
            | _ ->
                let container = HashSet [v]
                sortedSet.Add((k, container)) |> ignore
                sets.[k] <- container

    let remove (k : 'k) (v : 'v) =
        let _, self, _ = sortedSet.FindNeighbours((k, Unchecked.defaultof<_>))
   
        if self.HasValue then
            let (_,container) = self.Value

            if container.Count <= 0 then
                raise <| ArgumentException "invalid memory manager state"

            let res = container.Remove v

            // if the container just got empty we remove it from the
            // sorted set and the cache-dictionary
            if container.Count = 0 then
                sortedSet.Remove(k, container) |> ignore
                sets.Remove(k) |> ignore

            res
        else 
            false

    member x.TryGetGreaterOrEqual (minimal : 'k) = tryGet minimal
    member x.Insert (key : 'k, value : 'v) = insert key value
    member x.Remove (key : 'k, value : 'v) = remove key value
    member x.Clear() =
        sortedSet.Clear()
        sets.Clear()


module ReaderWriterLock =
    let read (l : ReaderWriterLockSlim) (f : unit -> 'a) =
        if l.IsReadLockHeld || l.IsWriteLockHeld then
            f()
        else
            try
                l.EnterReadLock()
                f()
            finally
                if l.IsReadLockHeld then
                    l.ExitReadLock()

    let write (l : ReaderWriterLockSlim) (f : unit -> 'a) =
        if l.IsWriteLockHeld then
            f()
        else
            try
                l.EnterWriteLock()
                f()
            finally
                if l.IsWriteLockHeld then
                    l.ExitWriteLock()


[<AllowNullLiteral>]
type Block =
    class
        val mutable public Offset : nativeint
        val mutable public Size : int
        val mutable public Prev : Block
        val mutable public Next : Block
        val mutable public Free : bool

        override x.ToString() =
            if x.Size < 0 then "invalid"
            else sprintf "[%d:%d]" x.Offset (x.Offset + nativeint x.Size)

        new(o,s,p,n,f) = { Offset = o; Size = s; Prev = p; Next = n; Free = f }
    end

and MemoryManager(capacity : int, malloc : int -> nativeint, mfree : nativeint -> int -> unit) =
    let mutable capacity = capacity
    let mutable ptr = malloc capacity
    let freeList = FreeList<int, Block>()
    let l = obj()
    let pointerLock = new ReaderWriterLockSlim()
    let structureLock = new ReaderWriterLockSlim()

    
    let mutable firstBlock = Block(0n, capacity, null, null, true)
    let mutable lastBlock = firstBlock
    do freeList.Insert(firstBlock.Size, firstBlock)

    let readPointer (f : unit -> 'a) = ReaderWriterLock.read pointerLock f
    let writePointer (f : unit -> 'a) = ReaderWriterLock.write pointerLock f
    let readStructure (f : unit -> 'a) = ReaderWriterLock.read structureLock f
    let writeStructure (f : unit -> 'a) = ReaderWriterLock.write structureLock f

    let destroy (b : Block) =
        b.Offset <- -1n
        b.Size <- -1
        b.Prev <- null
        b.Next <- null
        b.Free <- false

    let swap (l : Block) (r : Block) =
        if isNull l.Prev then firstBlock <- r
        else l.Prev.Next <- r
        if isNull l.Next then lastBlock <- r
        else l.Next.Prev <- r

        if isNull r.Prev then firstBlock <- l
        else r.Prev.Next <- l
        if isNull r.Next then lastBlock <- l
        else r.Next.Prev <- l

        Fun.Swap(&l.Offset, &r.Offset)
        Fun.Swap(&l.Size, &r.Size)
        Fun.Swap(&l.Prev, &r.Prev)
        Fun.Swap(&l.Next, &r.Next)
        Fun.Swap(&l.Free, &r.Free)




    let free (b : Block) : unit =
        assert (not (isNull b))

        if not b.Free && b.Size > 0 then
            writeStructure (fun () ->
                // merge b with its prev (if it's free)
                let prev = b.Prev
                if not (isNull prev) && prev.Free then 
                    if freeList.Remove(prev.Size, prev) then

                        // b now occupies the memory of both blocks
                        b.Offset <- prev.Offset
                        b.Size <- prev.Size + b.Size

                        // all links to prev now link to b
                        if isNull prev.Prev then firstBlock <- b
                        else prev.Prev.Next <- b

                        // b's prev now links wherever prev's prev linked to
                        b.Prev <- prev.Prev
                    
                        // destroy all of prev's fields
                        destroy prev

                    else
                        failwithf "[Memory] could not remove %A from freeList" prev

                // merge b with its next (if it's free)
                let next = b.Next
                if not (isNull next) && next.Free then 
                    if freeList.Remove(next.Size, next) then

                        // b now occupies the memory of both blocks
                        b.Size <- next.Size + b.Size

                        // all links to next now link to b
                        if isNull next.Next then lastBlock <- b
                        else next.Next.Prev <- b

                        // b's prev now links wherever prev's prev linked to
                        b.Next <- next.Next

                        // destroy all of next's fields
                        destroy next
                    else
                        failwithf "[Memory] could not remove %A from freeList" prev

                // tell b that it's free and add it to the freeList
                b.Free <- true
                freeList.Insert(b.Size, b)
            )

    let resize (additional : int) =
        assert (additional > 0)

        let oldCapacity = capacity
        let newCapacity = Fun.NextPowerOfTwo(capacity + additional)
        writePointer (fun () ->
            let n = malloc newCapacity
            Marshal.Copy(ptr, n, oldCapacity)

            mfree ptr capacity
            ptr <- n
            capacity <- newCapacity
        )

        writeStructure (fun () ->
            let newMemory = Block(nativeint oldCapacity, newCapacity - oldCapacity, lastBlock, null, false)
            lastBlock.Next <- newMemory
            lastBlock <- newMemory

            free newMemory
        )

    let rec alloc (size : int) : Block =
        if size <= 0 then
            Block(0n, 0, null, null, false)
        else
            match writeStructure (fun () -> freeList.TryGetGreaterOrEqual(size)) with
                | Some block ->
                    block.Free <- false

                    if block.Size > size then
                        writeStructure (fun () ->
                            let rest = Block(block.Offset + nativeint size, block.Size - size, block, block.Next, false)

                            if isNull rest.Next then lastBlock <- rest
                            else rest.Next.Prev <- rest
                            block.Next <- rest

                            block.Size <- size

                            free rest
                        )

                    block
                | None ->
                    // if there was no block of sufficient size resize the entire
                    // memory and retry
                    resize size
                    alloc size

    let rec realloc (b : Block) (size : int) : bool =
        writeStructure (fun () ->

            if b.Free then
                failwithf "[Memory] cannot realloc free block: %A" b

            elif size = 0 then
                // if a block gets re-allocated with size 0 it is now free
                
                let n = Block(b.Offset, b.Size, b.Prev, b.Next, b.Free)

                if isNull b.Prev then firstBlock <- n
                else b.Prev.Next <- n
                if isNull b.Next then lastBlock <- n
                else b.Next.Prev <- n

                free n

                b.Offset <- 0n
                b.Size <- 0
                b.Prev <- null
                b.Next <- null
                b.Free <- false


                false

            elif size > b.Size then

                if isNull b.Next || not b.Next.Free || b.Next.Size + b.Size < size then
                    let n = alloc size

                    writePointer (fun () -> Marshal.Copy(ptr + b.Offset, ptr + n.Offset, b.Size))
                    swap n b

                    free n

                    // alloc a completely new block and copy the contents there
                    true

                else
                    let next = b.Next
                    if freeList.Remove(next.Size, next) then
                        next.Free <- false

                        let additionalSize = size - b.Size
                        b.Size <- size

                        if next.Size > additionalSize then
                            // if next is larger than needed we free the rest
                            next.Offset <- b.Offset + nativeint size
                            next.Size <- next.Size - additionalSize

                            free next
                        else
                            // if next fits our requirements exactly it is deleted from the
                            // linked list and finally destroyed
                            b.Next <- next.Next
                            if isNull next.Next then lastBlock <- b
                            else next.Next.Prev <- b

                            destroy next


                        false
                    else 
                        failwith "[Memory] could not remove free block from freeList"

            elif size < b.Size then
                // if the block "shrinked" we can simply create and free the
                // leftover memory
                let rest = Block(b.Offset + nativeint size, b.Size - size, b, b.Next, false)
                b.Size <- size

                b.Next <- rest
                if isNull rest.Next then lastBlock <- rest
                else rest.Next.Prev <- rest

                free rest

                false

            else
                false
        )

    let spill (b : Block) : Block =
        if b.Free then failwithf "[Memory] cannot spill free block: %A" b

        writeStructure (fun () ->
            let n = alloc size

            writePointer (fun () -> Marshal.Copy(ptr + b.Offset, ptr + n.Offset, b.Size))
            swap n b

            n
        )

    member x.FirstBlock = firstBlock
    member x.LastBlock = lastBlock

    member x.Alloc (size : int) = lock l (fun () -> alloc size)
    member x.Free (block : Block) = lock l (fun () -> free block)
    member x.Realloc (block : Block, size : int) = lock l (fun () -> realloc block size)
    member x.Spill (block : Block) = lock l (fun () -> spill block)




