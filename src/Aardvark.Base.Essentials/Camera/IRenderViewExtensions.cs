namespace Aardvark.Base
{
    public static class IRenderViewExtensions
    {
        /// <summary>
        /// Projects a point from world-space to normalized device coordinates.
        /// </summary>
        public static Ndc3d Project(this IRenderView rv, V3d point)
        {
            var viewPoint = rv.View.ViewTrafo.Forward.TransformPos(point);
            return rv.Projection.TransformPos(viewPoint);
        }

        /// <summary>
        /// Project a point from world-space to a pixel position.
        /// </summary>
        public static PixelPosition ProjectPixel(this IRenderView rv, V3d point)
        {
            var ndcPoint = rv.Project(point);
            return new PixelPosition(ndcPoint, rv.Region);
        }

        /// <summary>
        /// Returns the number of pixels per unit on the near plane. 
        /// ISSUE: Does not consider ClippingWindow of projection
        /// </summary>
        public static V2d PixelsPerUnitOnNearPlane(this IRenderView rv)
        {
            if (rv.Projection is ICameraProjectionPerspective)
            {
                var unitsOnNearPlane = Fun.Tan(((ICameraProjectionPerspective)rv.Projection).HorizontalFieldOfViewInDegrees.RadiansFromDegrees() * 0.5) * 2 * rv.Projection.Near;
                return (V2d)rv.Region.Size / new V2d(unitsOnNearPlane, unitsOnNearPlane / rv.Projection.AspectRatio);
            }
            else
            {
                return (V2d)rv.Region.Size / rv.Projection.ClippingWindow.Size; 
            }
        }


        /// <summary>
        /// Returns the multiplied View-Projection transformation.
        /// </summary>
        public static Trafo3d ViewProjTrafo(this IViewProjection vp)
            => vp.View.ViewTrafo * vp.Projection.ProjectionTrafo;

        /// <summary>
        /// Builds a hull from the given view-projection (left, right, top, bottom, near, far).
        /// The normals of the hull planes point to the inside. 
        /// A point inside the visual hull will have positive height to all planes.
        /// </summary>
        public static Hull3d GetVisualHull(this IViewProjection vp)
            => vp.ViewProjTrafo().GetVisualHull();

        /// <summary>
        /// Builds a hull from the given view-projection transformation (left, right, top, bottom, near, far).
        /// The normals of the hull planes point to the inside. 
        /// A point inside the visual hull will have positive height to all planes.
        /// </summary>
        public static Hull3d GetVisualHull(this Trafo3d vpTrafo)
        {
            var frustumCorners = new Box3d(-V3d.IIO, V3d.III).ComputeCorners();

            //Min,                             0 near left bottom
            //new V3d(Max.X, Min.Y, Min.Z),    1 near right bottom
            //new V3d(Min.X, Max.Y, Min.Z),    2 near left top
            //new V3d(Max.X, Max.Y, Min.Z),    3 near right top
            //new V3d(Min.X, Min.Y, Max.Z),    4 far left bottom
            //new V3d(Max.X, Min.Y, Max.Z),    5 far right bottom
            //new V3d(Min.X, Max.Y, Max.Z),    6 far left top
            //Max                              7 far right top

            // use inverse view-projection to get vertices in world space
            frustumCorners.Apply(c => vpTrafo.Backward.TransformPosProj(c));

            // hull planes should point inward, assume right-handed transformation to build planes
            var hull = new Hull3d(new[]
            {
                new Plane3d(frustumCorners[0], frustumCorners[4], frustumCorners[2]), // left
                new Plane3d(frustumCorners[1], frustumCorners[3], frustumCorners[5]), // right
                new Plane3d(frustumCorners[2], frustumCorners[6], frustumCorners[3]), // top
                new Plane3d(frustumCorners[0], frustumCorners[1], frustumCorners[4]), // bottom
                new Plane3d(frustumCorners[0], frustumCorners[2], frustumCorners[1]), // near
                new Plane3d(frustumCorners[4], frustumCorners[5], frustumCorners[6]), // far
            });

            return hull;
        }

        /// <summary>
        /// Returns the ray-direction towards the center of the view.
        /// </summary>
        public static V3d CentralDirection(this IViewProjection vp)
        {
            var trafo = vp.View.ViewTrafo.Backward * vp.Projection.ProjectionTrafo.Backward;

            var nearPoint = trafo.TransformPosProj(V3d.OOO);
            var farPoint = trafo.TransformPosProj(V3d.OOI);

            return (farPoint - nearPoint).Normalized;
        }
    }
}
