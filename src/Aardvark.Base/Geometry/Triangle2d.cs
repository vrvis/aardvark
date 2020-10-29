using System.Runtime.CompilerServices;

namespace Aardvark.Base
{
    /// <summary>
    /// A two-dimensional triangle represented by its three points.
    /// </summary>
    public partial struct Triangle2d : IBoundingCircle2d
    {
        #region Geometric Properties

        /// <summary>
        /// Returns the area of the triangle.
        /// </summary>
        public double Area
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Triangle.Area(this);
        }

        /// <summary>
        /// Returns whether the triangle is degenerated, i.e. its area is zero.
        /// </summary>
        public bool IsDegenerated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Triangle.IsDegenerated(this);
        }

        /// <summary>
        /// Returns a negative value if the triangle has a
        /// counter-clockwise winding order, and a positive value if it has a clockwise winding-order.
        /// The magnitude is twice the area of the triangle.
        /// </summary>
        public double WindingOrder
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Triangle.WindingOrder(this);
        }

        #endregion

        #region CircumCircle

        public Circle2d CircumCircle
        {
            get
            {
                ComputeCircumCircleSquared(P0, P1, P2, out V2d center, out double radiusSquared);
                return new Circle2d(center, radiusSquared.Sqrt());
            }
        }

        public Circle2d CircumCircleSquared
        {
            get
            {
                ComputeCircumCircleSquared(P0, P1, P2, out V2d center, out double radiusSquared);
                return new Circle2d(center, radiusSquared);
            }
        }

        public static void ComputeCircumCircleSquared(
            V2d p0, V2d p1, V2d p2,
            out V2d center, out double radiusSquared)
        {
            double y01abs = System.Math.Abs(p0.Y - p1.Y);
            double y12abs = System.Math.Abs(p1.Y - p2.Y);

            if (y01abs < Constant<double>.PositiveTinyValue
                && y12abs < Constant<double>.PositiveTinyValue)
            {
                center = V2d.NaN; radiusSquared = -1.0; return;
            }

            double xc, yc;

            if (y01abs < Constant<double>.PositiveTinyValue)
            {
                double m2 = (p1.X - p2.X) / (p2.Y - p1.Y);
                double m12x = 0.5 * (p1.X + p2.X);
                double m12y = 0.5 * (p1.Y + p2.Y);
                xc = 0.5 * (p1.X + p0.X);
                yc = m2 * (xc - m12x) + m12y;
            }
            else if (y12abs < Constant<double>.PositiveTinyValue)
            {
                double m1 = (p0.X - p1.X) / (p1.Y - p0.Y);
                double m01x = 0.5 * (p0.X + p1.X);
                double m01y = 0.5 * (p0.Y + p1.Y);
                xc = 0.5 * (p2.X + p1.X);
                yc = m1 * (xc - m01x) + m01y;
            }
            else
            {
                double m1 = (p0.X - p1.X) / (p1.Y - p0.Y);
                double m2 = (p1.X - p2.X) / (p2.Y - p1.Y);
                double m01x = 0.5 * (p0.X + p1.X);
                double m01y = 0.5 * (p0.Y + p1.Y);
                double m12x = 0.5 * (p1.X + p2.X);
                double m12y = 0.5 * (p1.Y + p2.Y);
                double m12 = m1 - m2;
                if (System.Math.Abs(m12) < Constant<double>.PositiveTinyValue)
                {
                    center = V2d.NaN; radiusSquared = -1.0; return;
                }
                xc = (m1 * m01x - m2 * m12x + m12y - m01y) / m12;
                if (y01abs > y12abs)
                {
                    yc = m1 * (xc - m01x) + m01y;
                }
                else
                {
                    yc = m2 * (xc - m12x) + m12y;
                }
            }
            center = new V2d(xc, yc);
            radiusSquared = Vec.DistanceSquared(p0, center);
        }

        #endregion

        #region IBoundingCircle2d Members

        public Circle2d BoundingCircle2d
        {
            get
            {
                var edge01 = P1 - P0;
                var edge02 = P2 - P0;
                double dot0101 = Vec.Dot(edge01, edge01);
                double dot0102 = Vec.Dot(edge01, edge02);
                double dot0202 = Vec.Dot(edge02, edge02);
                double d = 2.0 * (dot0101 * dot0202 - dot0102 * dot0102);
                if (d.Abs() <= 0.000001) return Circle2d.Invalid;
                double s = (dot0101 * dot0202 - dot0202 * dot0102) / d;
                double t = (dot0202 * dot0101 - dot0101 * dot0102) / d;
                var p = P0;
                var cir = new Circle2d();
                if (s <= 0.0)
                    cir.Center = 0.5 * (P0 + P2);
                else if (t <= 0.0)
                    cir.Center = 0.5 * (P0 + P1);
                else if (s + t >= 1.0)
                {
                    cir.Center = 0.5 * (P1 + P2);
                    p = P1;
                }
                else
                    cir.Center = P0 + s * edge01 + t * edge02;
                cir.Radius = (cir.Center - p).Length;
                return cir;
            }
        }

        #endregion
    }

    /// <summary>
    /// Contains static methods for triangles.
    /// </summary>
    public static partial class Triangle
    {
        #region Area

        /// <summary>
        /// Returns the area of the triangle defined by the given points.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Area(V2d p0, V2d p1, V2d p2)
            => WindingOrder(p0, p1, p2).Abs() * 0.5;

        /// <summary>
        /// Returns the area of the given triangle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Area(Triangle2d t)
            => Area(t.P0, t.P1, t.P2);

        #endregion

        #region IsDegenerated

        /// <summary>
        /// Returns whether the triangle defined by the give points is degenerated, i.e. its area is zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDegenerated(V2d p0, V2d p1, V2d p2)
            => WindingOrder(p0, p1, p2).IsTiny();

        /// <summary>
        /// Returns whether the given triangle is degenerated, i.e. its area is zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDegenerated(Triangle2d t)
            => WindingOrder(t).IsTiny();

        #endregion

        #region WindingOrder

        /// <summary>
        /// Returns a negative value if the triangle defined by the given points has a
        /// counter-clockwise winding order, and a positive value if it has a clockwise winding-order.
        /// The magnitude is twice the area of the triangle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double WindingOrder(V2d p0, V2d p1, V2d p2)
            => (p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y);

        /// <summary>
        /// Returns a negative value if the given triangle has a
        /// counter-clockwise winding order, and a positive value if it has a clockwise winding-order.
        /// The magnitude is twice the area of the triangle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double WindingOrder(Triangle2d t)
            => WindingOrder(t.P0, t.P1, t.P2);

        #endregion
    }
}
