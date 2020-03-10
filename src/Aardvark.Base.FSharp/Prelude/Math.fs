﻿namespace Aardvark.Base

open System

/// Provides generic math functions that work for both scalars and vectors (element-wise).
/// Functions already provided by the F# core library are only redefined if necessary 
/// (e.g. different signature)
[<AutoOpen>]
module FSharpMath =

    module Helpers =
        type Comparison() =
            static member inline Min< ^a when ^a : comparison>(a : ^a, b : ^a) = Operators.min a b

            static member inline Min(a : float, b : V2d) = V2d.Min(b, a)
            static member inline Min(a : float, b : V3d) = V3d.Min(b, a)
            static member inline Min(a : float, b : V4d) = V4d.Min(b, a)
            static member inline Min(a : float32, b : V2f) = V2f.Min(b, a)
            static member inline Min(a : float32, b : V3f) = V3f.Min(b, a)
            static member inline Min(a : float32, b : V4f) = V4f.Min(b, a)
            static member inline Min(a : int, b : V2i) = V2i.Min(b, a)
            static member inline Min(a : int, b : V3i) = V3i.Min(b, a)
            static member inline Min(a : int, b : V4i) = V4i.Min(b, a)
            static member inline Min(a : int64, b : V2l) = V2l.Min(b, a)
            static member inline Min(a : int64, b : V3l) = V3l.Min(b, a)
            static member inline Min(a : int64, b : V4l) = V4l.Min(b, a)
            
            static member inline Max< ^a when ^a : comparison>(a : ^a, b : ^a) = Operators.max a b

            static member inline Max(a : float, b : V2d) = V2d.Max(b, a)
            static member inline Max(a : float, b : V3d) = V3d.Max(b, a)
            static member inline Max(a : float, b : V4d) = V4d.Max(b, a)
            static member inline Max(a : float32, b : V2f) = V2f.Max(b, a)
            static member inline Max(a : float32, b : V3f) = V3f.Max(b, a)
            static member inline Max(a : float32, b : V4f) = V4f.Max(b, a)
            static member inline Max(a : int, b : V2i) = V2i.Max(b, a)
            static member inline Max(a : int, b : V3i) = V3i.Max(b, a)
            static member inline Max(a : int, b : V4i) = V4i.Max(b, a)
            static member inline Max(a : int64, b : V2l) = V2l.Max(b, a)
            static member inline Max(a : int64, b : V3l) = V3l.Max(b, a)
            static member inline Max(a : int64, b : V4l) = V4l.Max(b, a)

        type Saturate() =
            static member inline Saturate(x : sbyte) = x |> max 0y |> min 1y
            static member inline Saturate(x : int16) = x |> max 0s |> min 1s
            static member inline Saturate(x : int32) = x |> max 0 |> min 1
            static member inline Saturate(x : int64) = x |> max 0L |> min 1L

            static member inline Saturate(x : byte) = x |> max 0uy |> min 1uy
            static member inline Saturate(x : uint16) = x |> max 0us |> min 1us
            static member inline Saturate(x : uint32) = x |> max 0u |> min 1u
            static member inline Saturate(x : uint64) = x |> max 0UL |> min 1UL

            static member inline Saturate(x : nativeint) = x |> max 0n |> min 1n
            static member inline Saturate(x : float) = x |> max 0.0 |> min 1.0
            static member inline Saturate(x : float32) = x |> max 0.0f |> min 1.0f
            static member inline Saturate(x : decimal) = x |> max 0m |> min 1m
            
        type Signs =
            static member inline Signum (a : ^a) =
                if a < LanguagePrimitives.GenericZero< ^a> then -LanguagePrimitives.GenericOne
                elif a > LanguagePrimitives.GenericZero< ^a> then LanguagePrimitives.GenericOne
                else LanguagePrimitives.GenericZero< ^a >
                
            static member inline Signumi (a : ^a) =
                if a < LanguagePrimitives.GenericZero< ^a> then -1
                elif a > LanguagePrimitives.GenericZero< ^a> then 1
                else 0

        type Power() =
            static member inline Power(a : ^a, b : ^b) = Operators.( ** ) a b
            
            static member inline Pown(a : ^a, b : int) = Operators.pown a b

        
        type Log2() =
            static member inline Log2(a : ^a) =
                log a / log (LanguagePrimitives.GenericOne + LanguagePrimitives.GenericOne)
            

    [<AutoOpen>]
    module private Aux =
        let inline signumAux (_ : ^z) (_ : ^y) (x : ^a) =
            ((^z or ^y or ^a) : (static member Signum : ^a  -> ^a) x)

        let inline signumiAux (_ : ^z) (_ : ^y) (x : ^a) =
            ((^z or ^y or ^a) : (static member Signumi : ^a  -> ^b) x)

        let inline powAux (_ : ^z) (_ : ^y) (x : ^a) (y : ^b) =
            ((^z or ^y or ^a or ^b or ^c) : (static member Power : ^a * ^b -> ^c) (x, y))

        let inline pownAux (_ : ^z) (_ : ^y) (x : ^a) (y : ^b) =
            ((^z or ^y or ^a or ^b or ^c) : (static member Pown : ^a * ^b -> ^c) (x, y))

        let inline log2Aux (_ : ^z) (_ : ^y) (x : ^a) =
            ((^z or ^y or ^a) : (static member Log2 : ^a  -> ^a) x)

        let inline cbrtAux (_ : ^z) (x : ^a) =
            ((^z or ^a) : (static member Cbrt : ^a  -> ^a) x)
        
        let inline minAux (_ : ^z) (x : ^a) (y : ^b) =
            ((^z or ^a or ^b or ^c) : (static member Min : ^a * ^b -> ^c) (x, y))

        let inline maxAux (_ : ^z) (x : ^a) (y : ^b) =
            ((^z or ^a or ^b or ^c) : (static member Max : ^a * ^b -> ^c) (x, y)) 
            
        // Simply using min and max directly will resolve to the comparison overload for some reason.
        // Therefore we need to do it the dumb (incomplete) way. E.g. won't work for Version.
        let inline saturateAux (_ : ^z) (x : ^a) =
            ((^z or ^a) : (static member Saturate : ^a -> ^a) (x))

        let inline lerpAux (_ : ^z) (x : ^a) (y : ^a) (t : ^b) =
            ((^z or ^a or ^b) : (static member Lerp : ^b * ^a * ^a -> ^a) (t, x, y))

        let inline smoothstepAux (_ : ^z) (edge0 : ^a) (edge1 : ^a) (x : ^b) =
            ((^z or ^a or ^b) : (static member Smoothstep : ^b * ^a * ^a -> ^b) (x, edge0, edge1))

    /// Resolves to the zero value for any scalar or vector type.
    [<GeneralizableValue>]
    let inline zero< ^T when ^T : (static member Zero : ^T) > : ^T =
        LanguagePrimitives.GenericZero

    /// Resolves to the one value for any scalar or vector type.
    [<GeneralizableValue>]
    let inline one< ^T when ^T : (static member One : ^T) > : ^T =
        LanguagePrimitives.GenericOne
        
    /// Returns -1 if x is less than zero, 0 if x is equal to zero, and 1 if
    /// x is greater than zero. The result has the same type as the input.
    let inline signum x =
        signumAux Unchecked.defaultof<Fun> Unchecked.defaultof<Helpers.Signs> x

    /// Returns -1 if x is less than zero, 0 if x is equal to zero, and 1 if
    /// x is greater than zero.
    let inline signumi x =
        signumiAux Unchecked.defaultof<Fun> Unchecked.defaultof<Helpers.Signs> x

    /// Returns x raised by the power of y (must be float or double).
    // F# variant does not support integers!
    let inline pow x y =
        powAux Unchecked.defaultof<Fun> Unchecked.defaultof<Helpers.Power> x y

    /// Returns x raised by the integer power of y (must not be negative).
    // F# variant has signature a' -> b' -> 'a, which does not permit for example float32 -> V2i -> V2f
    let inline pown x y =
        pownAux Unchecked.defaultof<Fun> Unchecked.defaultof<Helpers.Power> x y

    /// Returns x raised by the power of y.
    // F# variant does not support integers!
    let inline ( ** ) x y =
        powAux Unchecked.defaultof<Fun> Unchecked.defaultof<Helpers.Power> x y

    /// Returns the base 2 logarithm of x.
    let inline log2 x = 
        log2Aux Unchecked.defaultof<Fun> Unchecked.defaultof<Helpers.Log2> x

    /// Returns x^2
    let inline sqr x = x * x

    /// Returns the cubic root of x.
    let inline cbrt x =
        cbrtAux Unchecked.defaultof<Fun> x

    /// Returns the smaller of x and y.
    let inline min x y = minAux Unchecked.defaultof<Helpers.Comparison> x y

    /// Returns the larger of x and y.
    let inline max x y = maxAux Unchecked.defaultof<Helpers.Comparison> x y

    /// Clamps x to the interval [a, b].
    let inline clamp a b x =
        x |> max a |> min b

    /// Clamps x to the interval [0, 1].
    let inline saturate (x : ^a) =
        saturateAux Unchecked.defaultof<Helpers.Saturate> x

    /// Linearly interpolates between x and y.
    let inline lerp x y t =
        lerpAux Unchecked.defaultof<Fun> x y t

    /// Performs Hermite interpolation between a and b.
    let inline smoothstep (edge0 : ^a) (edge1 : ^a) (x : ^b) =
        smoothstepAux Unchecked.defaultof<Fun> edge0 edge1 x


    [<CompilerMessage("testing purposes", 1337, IsHidden = true)>]
    module ``Math compiler tests 😀😁`` = 
        type MyCustomNumericTypeExtensionTestTypeForInternalTesting() =
            static member Pow(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting, e : float) = h
            static member (*)(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting, e : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = h
            static member (/)(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting, e : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = h
            static member One = MyCustomNumericTypeExtensionTestTypeForInternalTesting()

            static member Signum(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = h
            static member Signumi(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = 0
            static member Cbrt(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = h

            static member Log(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = h
            static member Log2(h : MyCustomNumericTypeExtensionTestTypeForInternalTesting) = h

        let fsharpCoreWorking() =

            let absWorking() = 
                let a : V2i = abs V2i.One
                let a : V3f = abs V3f.One
                ()

            let acosWorking() =
                let a : V2f = acos V2f.One
                let a : V3d = acos V3d.One
                let a : ComplexD = acos ComplexD.One
                ()

            let asinWorking() =
                let a : V2f = asin V2f.One
                let a : V3d = asin V3d.One
                let a : ComplexD = asin ComplexD.One
                ()

            let atanWorking() =
                let a : V2f = atan V2f.One
                let a : V3d = atan V3d.One
                let a : ComplexD = atan ComplexD.One
                ()

            let atan2Working() =
                let a : V2f = atan2 V2f.One V2f.Zero
                let a : V3d = atan2 V3d.One V3d.Zero
                ()

            let ceilWorking() =
                let a : V2f = ceil V2f.One
                let a : V3d = ceil V3d.One
                ()

            let expWorking() =
                let a : V2f = exp V2f.One
                let a : V3d = exp V3d.One
                let a : ComplexD = exp ComplexD.One
                ()

            let floorWorking() =
                let a : V2f = floor V2f.One
                let a : V3d = floor V3d.One
                ()

            let truncateWorking() =
                let a : V2f = truncate V2f.One
                let a : V3d = truncate V3d.One
                ()

            let roundWorking() =
                let a : V2f = round V2f.One
                let a : V3d = round V3d.One
                ()

            let logWorking() =
                let a : V2f = log V2f.One
                let a : V3d = log V3d.One
                let a : ComplexD = log ComplexD.One
                ()

            let log10Working() =
                let a : V2f = log10 V2f.One
                let a : V3d = log10 V3d.One
                let a : ComplexD = log10 ComplexD.One
                ()

            let sqrtWorking() =
                let a : V2f = sqrt V2f.One
                let a : V3d = sqrt V3d.One
                let a : ComplexD = sqrt ComplexD.One
                ()

            let cosWorking() =
                let a : V2f = cos V2f.One
                let a : V3d = cos V3d.One
                let a : ComplexD = cos ComplexD.One
                ()

            let coshWorking() =
                let a : V2f = cosh V2f.One
                let a : V3d = cosh V3d.One
                let a : ComplexD = cosh ComplexD.One
                ()

            let sinWorking() =
                let a : V2f = sin V2f.One
                let a : V3d = sin V3d.One
                let a : ComplexD = sin ComplexD.One
                ()

            let sinhWorking() =
                let a : V2f = sinh V2f.One
                let a : V3d = sinh V3d.One
                let a : ComplexD = sinh ComplexD.One
                ()

            let tanWorking() =
                let a : V2f = tan V2f.One
                let a : V3d = tan V3d.One
                let a : ComplexD = tan ComplexD.One
                ()

            let tanhWorking() =
                let a : V2f = tanh V2f.One
                let a : V3d = tanh V3d.One
                let a : ComplexD = tanh ComplexD.One
                ()

            ()

        let zeroWorking() =
            let a : int = zero
            let a : float = zero
            let a : V2d = zero
            let a : ComplexD = zero
            ()

        let oneWorking() =
            let a : int = one
            let a : float = one
            let a : V2d = one
            let a : ComplexD = one
            ()

        let signumWorking() =
            let a : float = signum 1.0
            let b : int = signum 1
            let s : V2d = signum V2d.II
            let a : decimal = signum 1.0m
            let a : MyCustomNumericTypeExtensionTestTypeForInternalTesting = signum (MyCustomNumericTypeExtensionTestTypeForInternalTesting())
            ()

        let signumIntWorking() =
            let a : int = signumi 1.0
            let b : int = signumi 1
            let s : V2i = signumi V2d.II
            let a : int = signumi (MyCustomNumericTypeExtensionTestTypeForInternalTesting())
            ()

        let pownWorking() =
            let a = pown (MyCustomNumericTypeExtensionTestTypeForInternalTesting()) 6
            let a : float = pown 1.0 2
            let a : int = pown 1 2
            let a : uint16 = pown 1us 2
            let a : uint16 = pown 1us 2us
            let a : int64 = pown 1L 2
            let a : int64 = pown 1L 2L

            let a : V2f = pown V2f.One 1
            let a : V2f = pown 1.0f V2i.One
            let a : V2f = pown V2f.One V2i.One
            
            let a : V2i = pown V2i.One 1
            let a : V2i = pown 1 V2i.One
            let a : V2i = pown V2i.One V2i.One

            let a : V2l = pown V2l.One 1
            let a : V2l = pown 1L V2i.One
            let a : V2l = pown V2l.One 1L
            let a : V2l = pown 1L V2l.One
            let a : V2l = pown V2l.One V2i.One
            let a : V2l = pown V2l.One V2l.One

            ()

        let powWorking() =
            let a : float = pow 1.0 2.0
            let a : float = pow 1 2.0
            let a : float = pow 1uy 2.0
            let a : float = pow 1s 2.0
            let a : float = pow 1L 2.0

            let a = pow (MyCustomNumericTypeExtensionTestTypeForInternalTesting()) 12.0

            let a : V2d = pow V2d.II V2d.II
            let a : V2d = pow V2d.II 1.0
            let a : V2d = pow 1.0 V2d.II

            let a : V2d = pow V2i.II 1.0
            let a : V2d = pow 1 V2d.II
            let a : V2d = pow V2i.II V2d.II
            let a : V2f = pow V2i.II 1.0f
            let a : V2f = pow 1 V2f.II
            let a : V2f = pow V2i.II V2f.II

            let a : V3d = pow V3l.III 1.0
            let a : V3d = pow 1L V3d.III
            let a : V3d = pow V3l.III V3d.III
            let a : V3f = pow V3l.III 1.0f
            let a : V3f = pow 1L V3f.III
            let a : V3f = pow V3l.III V3f.III

            let a : ComplexD = pow ComplexD.One ComplexD.One
            let a : ComplexD = pow ComplexD.One 1.0
            let a : ComplexD = pow 1.0 ComplexD.One
            ()

        let powOpWorking() =
            let a : float = 1.0 ** 2.0
            let a : float = 1 ** 2.0
            let a : float = 1uy ** 2.0
            let a : float = 1s ** 2.0
            let a : float = 1L ** 2.0
            
            let a = MyCustomNumericTypeExtensionTestTypeForInternalTesting() ** 12.0

            let a : V2d = V2d.II ** V2d.II
            let a : V2d = V2d.II ** 1.0
            let a : V2d = 1.0 ** V2d.II

            let a : V2d = V2i.II ** 1.0
            let a : V2d = 1 ** V2d.II
            let a : V2d = V2i.II ** V2d.II
            let a : V2f = V2i.II ** 1.0f
            let a : V2f = 1 ** V2f.II
            let a : V2f = V2i.II ** V2f.II

            let a : V3d = V3l.III ** 1.0
            let a : V3d = 1L ** V3d.III
            let a : V3d = V3l.III ** V3d.III
            let a : V3f = V3l.III ** 1.0f
            let a : V3f = 1L ** V3f.III
            let a : V3f = V3l.III ** V3f.III

            let a : ComplexD = ComplexD.One ** ComplexD.One
            let a : ComplexD = ComplexD.One ** 1.0
            let a : ComplexD = 1.0 ** ComplexD.One
            ()
            
        let log2Working() =
            let a : float = log2 10.0
            let a : V2d =  log2 V2d.II
            let a : ComplexD = log2 ComplexD.One
            let a : MyCustomNumericTypeExtensionTestTypeForInternalTesting = log2 (MyCustomNumericTypeExtensionTestTypeForInternalTesting())
            ()
            
        let cbrtWorking() =
            let a : float = cbrt 10.0
            let a : V2d =  cbrt V2d.II
            let a : ComplexD = cbrt ComplexD.One
            let a : MyCustomNumericTypeExtensionTestTypeForInternalTesting = cbrt (MyCustomNumericTypeExtensionTestTypeForInternalTesting())
            ()
            
        let sqrWorking() =
            let a : byte = sqr 4uy
            let a : float = sqr 10.0
            let a : V2d = sqr V2d.II
            let a : V2i = sqr V2i.II
            let a : ComplexD = sqr ComplexD.One
            ()

        let clampWorking() =
            let a : int = clamp 1 2 3
            let a : Version = clamp (Version(1,2,3)) (Version(3,2,3)) (Version(4,5,6))
            let a : V2d = clamp V2d.Zero V2d.One V2d.Half
            let a : V3f = clamp V3f.Zero 0.5f 1.5f
            ()

        let minWorking() =
            let a : V2d = min V2d.II V2d.OO
            let a : V2d = min V2d.II 0.0
            let a : V2d = min 0.0 V2d.II
            let a : float = min 1.0 2.0
            let a : uint32 = min 1u 2u
            let a : nativeint = min 1n 2n
            let a : Version = min (Version(1,2,3)) (Version(3,2,3))
            ()

        let maxWorking() =
            let a : V2d = max V2d.II V2d.OO
            let a : V2d = max V2d.II 0.0
            let a : V2d = max 0.0 V2d.II
            let a : float = max 1.0 2.0
            let a : uint32 = max 1u 2u
            let a : nativeint = max 1n 2n
            let a : Version = max (Version(1,2,3)) (Version(3,2,3))
            ()

        let saturateWorking() =
            let a : int = saturate 3
            let a : float = saturate 3.0
            let a : uint32 = saturate 3u
            let a : nativeint = saturate 3n
            let a : V2d = saturate V2d.One
            let a : V4i = saturate V4i.One
            ()

        let lerpWorking() =
            let a : int = lerp 1 10 0.5
            let a : int = lerp 1 10 0.5f
            let a : float = lerp 1.0 10.0 0.5
            let a : float32 = lerp 1.0f 10.0f 0.5f
            let a : V2i = lerp V2i.Zero V2i.One 0.5
            let a : V4i = lerp V4i.Zero V4i.One V4d.Half
            let a : V2i = lerp V2i.Zero V2i.One 0.5f
            let a : V4i = lerp V4i.Zero V4i.One V4f.Half
            ()

        let smoothstepWorking() =
            let a : float = smoothstep 0.0 1.0 0.5
            let a : float32 = smoothstep 0.0f 1.0f 0.5f
            let a : V2f = smoothstep 0.0f 1.0f V2f.Half
            let a : V4d = smoothstep V4d.Zero V4d.One V4d.Half
            ()