﻿namespace Aardvark.Geometry

open System
open Aardvark.Base
open System.Collections.Generic

[<AutoOpen>]
module private Regression3dHelpers =

    let inline sortTripleAbs (struct(a,b,c)) =
        let aa = abs a
        let bb = abs b
        let cc = abs c
        if aa < bb then   
            // a < b
            if bb < cc then struct(a,b,c)
            elif aa < cc then struct(a,c,b)
            else struct(c,a,b)
        else
            // b < a
            if aa < cc then struct(b,a,c)
            elif bb < cc then struct(b,c,a)
            else struct(c,b,a)

[<Struct>]
type Regression3d =
    
    val mutable private _reference : V3d
    val mutable private _sum : V3d
    val mutable private _sumSq : V3d
    val mutable private _off : V3d
    val mutable private _count : int

    private new(reference : V3d, sum : V3d, sumSq : V3d, off : V3d, count : int) =
        { _reference = reference; _sum = sum; _sumSq = sumSq; _off = off; _count = count }

    /// Creates a new regression with a single point.
    new(point : V3d) =
        { _reference = point; _sum = V3d.Zero; _sumSq = V3d.Zero; _off = V3d.Zero; _count = 1 }
        
    /// Creates a new regression from the given points.
    new([<ParamArray>] points : V3d[]) =
        if points.Length = 0 then
            { _reference = V3d.Zero; _sum = V3d.Zero; _sumSq = V3d.Zero; _off = V3d.Zero; _count = 0 }
        else
            let reference = points.[0]
            let mutable sum = V3d.Zero
            let mutable sumSq = V3d.Zero
            let mutable off = V3d.Zero
            let mutable count = 1

            for i in 1 .. points.Length - 1 do
                let pt = points.[i] - reference
                sum <- sum + pt
                sumSq <- sumSq + sqr pt
                off <- off + V3d(pt.Y*pt.Z, pt.X*pt.Z, pt.X*pt.Y)
                count <- count + 1
            
            { _reference = reference; _sum = sum; _sumSq = sumSq; _off = off; _count = count }
        
    /// Creates a new regression from the given points.
    new(s : seq<V3d>) = Regression3d(Seq.toArray s)
    
    /// Creates a new regression from the given points.
    new(s : list<V3d>) = Regression3d(List.toArray s)


    /// A regression holding no points.
    static member Zero = Regression3d(V3d.Zero, V3d.Zero, V3d.Zero, V3d.Zero, 0)

    /// A regression holding no points.
    static member Empty = Regression3d(V3d.Zero, V3d.Zero, V3d.Zero, V3d.Zero, 0)
    
    /// The total number of points added. Note that at least 3 points are required to fit a plane.
    member x.Count = x._count

    member private x.ReferencePoint = x._reference
    member private x.Sum = x._sum
    member private x.SumSq = x._sum
    member private x.OffDiag = x._off

    member private x.WithReferencePoint(r : V3d) =
        let d = x._reference - r
        if r + d = r then
            x
        else
            let n = float x._count

            // sum ((xi+dx)*(yi+dy))
            // sum (xi*yi + dx*yi + dy*xi + dx*dy)
            // sum (xi*yi) + dx*sum(yi) + dy*sum(xi) + n*dx*dy
            let vxy = d.X*x._sum.Y + d.Y*x._sum.X + n*d.X*d.Y
            let vxz = d.X*x._sum.Z + d.Z*x._sum.X + n*d.X*d.Z
            let vyz = d.Y*x._sum.Z + d.Z*x._sum.Y + n*d.Y*d.Z

            // sum (xi+d)^2 
            // sum (xi^2 + 2xi*d + d^2)
            // sum (xi^2) + 2*d*sum(xi) + n*d^2

            Regression3d(
                r, 
                x._sum + n*d,
                x._sumSq + 2.0*d*x._sum + n*sqr d,
                x._off + V3d(vyz, vxz, vxy),
                x._count
            )

    /// The centroid of all added points.
    member x.Centroid = 
        if x._count = 0 then V3d.Zero
        else x._reference + x._sum / float x._count

    /// Adds the given point to the regression.
    member x.Add(pt : V3d) =
        if x._count = 0 then
            Regression3d(pt, V3d.Zero, V3d.Zero, V3d.Zero, 1)
        else
            let pt = pt - x._reference
            Regression3d(
                x._reference,
                x._sum + pt,
                x._sumSq + sqr pt,
                x._off + V3d(pt.Y*pt.Z, pt.X*pt.Z, pt.X*pt.Y),
                x._count + 1
            )

    /// Adds the given point to the regression (mutating the regression).
    member x.AddInPlace(pt : V3d) =
        if x._count = 0 then
            x._reference <- pt
            x._sum <- V3d.Zero
            x._sumSq <- V3d.Zero
            x._off <- V3d.Zero
            x._count <- 1
        else
            let pt = pt - x._reference
            x._sum <- x._sum + pt
            x._sumSq <- x._sumSq + sqr pt
            x._off <- x._off + V3d(pt.Y*pt.Z, pt.X*pt.Z, pt.X*pt.Y)
            x._count <- x._count + 1
            
            
    /// Removes the given point from the regression assuming that it has previously been added.
    /// NOTE: when removing non-added points the regression will produce inconsistent results.
    member x.Remove(pt : V3d) =
        if x._count <= 1 then
            Regression3d.Empty
        else
            let pt = pt - x._reference
            Regression3d(
                x._reference,
                x._sum - pt,
                x._sumSq - sqr pt |> max V3d.Zero,
                x._off - V3d(pt.Y*pt.Z, pt.X*pt.Z, pt.X*pt.Y),
                x._count - 1
            )
 
    /// Removes the given point from the regression assuming that it has previously been added.
    /// NOTE: when removing non-added points the regression will produce inconsistent results.
    member x.RemoveInPlace(pt : V3d) =
        if x._count <= 1 then
            x._reference <- V3d.Zero
            x._sum <- V3d.Zero
            x._sumSq <- V3d.Zero
            x._off <- V3d.Zero
            x._count <- 0
        else
            let pt = pt - x._reference
            x._sum <- x._sum - pt
            x._sumSq <- x._sumSq - sqr pt
            x._off <- x._off - V3d(pt.Y*pt.Z, pt.X*pt.Z, pt.X*pt.Y)
            x._count <- x._count - 1


    /// Gets the variances as [Var(X), Var(Y), Var(Z)]
    member x.Variance =
        if x._count < 2 then
            V3d.Zero
        else
            let n = float x._count
            let avg = x._sum / n
            let xx = (x._sumSq.X - avg.X * x._sum.X) / (n - 1.0)
            let yy = (x._sumSq.Y - avg.Y * x._sum.Y) / (n - 1.0)
            let zz = (x._sumSq.Z - avg.Z * x._sum.Z) / (n - 1.0)
            V3d(xx, yy, zz)

    /// Gets the covariances as [Cov(Y,Z), Cov(X,Z), Cov(X,Y)]
    member x.Covariance =
        if x._count < 2 then
            V3d.Zero
        else
            let n = float x._count
            let avg = x._sum / n
            let xy = (x._off.Z - avg.X * x._sum.Y) / (n - 1.0)
            let xz = (x._off.Y - avg.X * x._sum.Z) / (n - 1.0)
            let yz = (x._off.X - avg.Y * x._sum.Z) / (n - 1.0)
            V3d(yz, xz, xy)

    /// Gets the covariance matrix as
    /// | Cov(X,X) | Cov(X,Y) | Cov(X,Z) |
    /// | Cov(X,Y) | Cov(Y,Y) | Cov(Y,Z) |
    /// | Cov(X,Z) | Cov(Y,Z) | Cov(Z,Z) |
    member x.CovarianceMatrix =
        if x._count < 2  then
            M33d.Zero
        else
            let n = float x._count
            let avg = x._sum / n
            let xx = (x._sumSq.X - avg.X * x._sum.X) / (n - 1.0)
            let yy = (x._sumSq.Y - avg.Y * x._sum.Y) / (n - 1.0)
            let zz = (x._sumSq.Z - avg.Z * x._sum.Z) / (n - 1.0)

            let xy = (x._off.Z - avg.X * x._sum.Y) / (n - 1.0)
            let xz = (x._off.Y - avg.X * x._sum.Z) / (n - 1.0)
            let yz = (x._off.X - avg.Y * x._sum.Z) / (n - 1.0)
        
            M33d(
                xx, xy, xz,
                xy, yy, yz,
                xz, yz, zz
            )

    /// Gets the least-squares plane and a confidence value in [0,1] where 1 stands for a perfect plane.
    member x.GetPlaneAndConfidence() =
        if x._count < 3 then
            struct(Plane3d.Invalid, 0.0)
        else
            let n = float x._count
            let avg = x._sum / n
            let xx = (x._sumSq.X - avg.X * x._sum.X) / (n - 1.0)
            let yy = (x._sumSq.Y - avg.Y * x._sum.Y) / (n - 1.0)
            let zz = (x._sumSq.Z - avg.Z * x._sum.Z) / (n - 1.0)

            let xy = (x._off.Z - avg.X * x._sum.Y) / (n - 1.0)
            let xz = (x._off.Y - avg.X * x._sum.Z) / (n - 1.0)
            let yz = (x._off.X - avg.Y * x._sum.Z) / (n - 1.0)
            
            let _a = 1.0
            let b = -xx - yy - zz
            let c = -sqr xy - sqr xz - sqr yz + xx*yy + xx*zz + yy*zz
            let d = -xx*yy*zz - 2.0*xy*xz*yz + sqr xz*yy + sqr xy*zz + sqr yz*xx


            let struct (l0, l1, _l2) = sortTripleAbs (Polynomial.RealRootsOfNormed(b,c,d))
            if Fun.IsTiny l1 then
                struct(Plane3d.Invalid, 0.0)
            else
                let goodness = 1.0 - abs l0 / abs l1
                
                if goodness > 1E-5 then
                    let c0 = V3d(xx - l0, xy, xz)
                    let c1 = V3d(xy, yy - l0, yz)
                    let c2 = V3d(xz, yz, zz - l0)
                    let normal = 
                        let len0 = Vec.lengthSquared c0
                        let len1 = Vec.lengthSquared c1
                        let len2 = Vec.lengthSquared c2

                        if len0 > len1 then
                            if len2 > len1 then Vec.cross c0 c2 |> Vec.normalize
                            else Vec.cross c0 c1 |> Vec.normalize
                        else
                            if len2 > len0 then Vec.cross c1 c2 |> Vec.normalize
                            else Vec.cross c0 c1 |> Vec.normalize

                    struct(Plane3d(normal, x._reference + avg), goodness)
                else
                    struct(Plane3d.Invalid, goodness)

    /// Gets a Trafo3d for the entire plane (using its principal components) and the respective standard deviations for each axis.
    member x.GetTrafoAndSizes() =
        if x._count < 3 then
            struct(Trafo3d.Identity, V3d.Zero)
        else
            let n = float x._count
            let avg = x._sum / n
            let xx = (x._sumSq.X - avg.X * x._sum.X) / (n - 1.0)
            let yy = (x._sumSq.Y - avg.Y * x._sum.Y) / (n - 1.0)
            let zz = (x._sumSq.Z - avg.Z * x._sum.Z) / (n - 1.0)

            let xy = (x._off.Z - avg.X * x._sum.Y) / (n - 1.0)
            let xz = (x._off.Y - avg.X * x._sum.Z) / (n - 1.0)
            let yz = (x._off.X - avg.Y * x._sum.Z) / (n - 1.0)
            
            let _a = 1.0
            let b = -xx - yy - zz
            let c = -sqr xy - sqr xz - sqr yz + xx*yy + xx*zz + yy*zz
            let d = -xx*yy*zz - 2.0*xy*xz*yz + sqr xz*yy + sqr xy*zz + sqr yz*xx

            let struct (l0, l1, l2) = sortTripleAbs (Polynomial.RealRootsOfNormed(b,c,d))
            if Fun.IsTiny l1 then
                struct (Trafo3d.Identity, x._reference + avg)
            else
                let c0 = V3d(xx - l0, xy, xz)
                let c1 = V3d(xy, yy - l0, yz)
                let c2 = V3d(xz, yz, zz - l0)
                let struct(vx, vy, vz) = 
                    let len0 = Vec.lengthSquared c0
                    let len1 = Vec.lengthSquared c1
                    let len2 = Vec.lengthSquared c2

                    if len0 > len1 then
                        if len1 > len2 then struct(c0, c1, Vec.cross c0 c1)
                        elif len0 > len2 then struct(c0, c2, Vec.cross c0 c2)
                        else struct(c2, c0, Vec.cross c2 c0)
                    else (* len1 >= len0*)
                        if len0 > len2 then struct(c1, c0, Vec.cross c1 c0)
                        elif len1 > len2 then struct(c1, c2, Vec.cross c1 c2)
                        else struct(c2, c1, Vec.cross c2 c1)

                // orthonormalize the system (minimal numerical errors)
                let vz = Vec.normalize vz
                let vx = Vec.normalize (vx - vz * Vec.dot vx vz)
                let vy = Vec.normalize (vy - vz * Vec.dot vy vz - vx * Vec.dot vy vx)

                // get the stddevs for the main axes
                let s0 = if l0 < 0.0 then 0.0 else sqrt l0
                let s1 = if l1 < 0.0 then 0.0 else sqrt l1
                let s2 = if l2 < 0.0 then 0.0 else sqrt l2
                let dev = V3d(s2, s1, s0)

                let trafo =
                    let o = x._reference + avg
                    Trafo3d(
                        M44d(
                            vx.X, vy.X, vz.X, o.X,
                            vx.Y, vy.Y, vz.Y, o.Y,
                            vx.Z, vy.Z, vz.Z, o.Z,
                            0.0,  0.0,  0.0,  1.0
                        ),
                        M44d(
                            vx.X, vx.Y, vx.Z, -Vec.dot vx o,
                            vy.X, vy.Y, vy.Z, -Vec.dot vy o,
                            vz.X, vz.Y, vz.Z, -Vec.dot vz o,
                            0.0,  0.0,  0.0,   1.0
                        )
                    )
                struct(trafo, dev)
    
    /// Gets the least-squares plane.
    member x.GetPlane() =
        let struct(p, _) = x.GetPlaneAndConfidence()
        p
        
    /// Gets a Trafo3d for the entire plane using its principal components.
    member x.GetTrafo() =
        let struct(t, _) = x.GetTrafoAndSizes()
        t

    static member (+) (l : Regression3d, r : Regression3d) =
        if r.Count <= 0 then l
        elif l.Count <= 0 then r
        elif l.Count = 1 then r.Add l.ReferencePoint
        elif r.Count = 1 then l.Add r.ReferencePoint
        else
            let r1 = r.WithReferencePoint l.ReferencePoint
            Regression3d(
                l._reference,
                l._sum + r1._sum,
                l._sumSq + r1._sumSq,
                l._off + r1._off,
                l._count + r1._count
            )
        
    static member (-) (l : Regression3d, r : Regression3d) =
        if r.Count <= 0 then l
        elif l.Count <= r.Count then Regression3d.Zero
        elif r.Count = 1 then l.Remove r.ReferencePoint
        else
            let r = r.WithReferencePoint l.ReferencePoint
            Regression3d(
                l.ReferencePoint,
                l.Sum - r.Sum,
                l.SumSq - r.SumSq,
                l.OffDiag - r.OffDiag,
                l.Count - r.Count
            )
        
    static member (+) (l : Regression3d, r : V3d) =
        l.Add r
        
    static member (+) (l : V3d, r : Regression3d) =
        r.Add l
        
    static member (-) (l : Regression3d, r : V3d) =
        l.Remove r

module Regression3d =

    /// A regression holding no points.
    let empty = Regression3d.Zero

    /// Removes the given point from the regression assuming that it has previously been added.
    /// NOTE: when removing non-added points the regression will produce inconsistent results.
    let inline remove (pt : V3d) (s : Regression3d) = s.Remove pt

    /// Adds the given point to the regression.
    let inline add (pt : V3d) (s : Regression3d) = s.Add pt

    /// Gets the least-squares plane.
    let inline getPlane (s : Regression3d) = s.GetPlane()

    /// Gets a Trafo3d for the entire plane using its principal components.
    let inline getTrafo (s : Regression3d) = s.GetTrafo()
    
    /// The total number of points added. Note that at least 3 points are required to fit a plane.
    let inline count (s : Regression3d) = s.Count
    
    /// The centroid of all added points.
    let inline centroid (s : Regression3d) = s.Centroid
    
    /// Gets the variances as [Var(X), Var(Y), Var(Z)]
    let inline variance (s : Regression3d) = s.Variance

    /// Gets the covariances as [Cov(Y,Z), Cov(X,Z), Cov(X,Y)]
    let inline covariance (s : Regression3d) = s.Covariance

    /// Gets the covariance matrix as
    /// | Cov(X,X) | Cov(X,Y) | Cov(X,Z) |
    /// | Cov(X,Y) | Cov(Y,Y) | Cov(Y,Z) |
    /// | Cov(X,Z) | Cov(Y,Z) | Cov(Z,Z) |
    let inline covarianceMatrix (pt : V3d) (s : Regression3d) = s.CovarianceMatrix

    /// Creates a regression from the given points.
    let ofSeq (points : seq<V3d>) =
        let mutable r = empty
        for p in points do r.AddInPlace p
        r
        
    /// Creates a regression from the given points.
    let ofList (points : list<V3d>) =
        let mutable r = empty
        for p in points do r.AddInPlace p
        r
        
    /// Creates a regression from the given points.
    let ofArray (points : V3d[]) =
        let mutable r = empty
        for p in points do r.AddInPlace p
        r

