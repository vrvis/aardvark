using System;

namespace Aardvark.Base
{
    public static class ICameraProjectionExtensions
    {
        /// <summary>
        /// Gets line (in camera space) from point on near plane to corresponding point on far plane.
        /// </summary>
        public static Line3d Unproject(this ICameraProjection self, Ndc2d p)
            => new Line3d(
                self.UnprojectPointOnNearPlane(p),
                self.UnprojectPointOnFarPlane(p)
                );

        /// <summary>
        /// Gets point (in camera space) from point in normalized device coordinates.
        /// </summary>
        public static V3d Unproject(this ICameraProjection self, Ndc3d p)
            => self.ProjectionTrafo.Backward.TransformPosProj(p.Position);

        /// <summary>
        /// Gets point on near plane (in camera space) from point in normalized device coordinates.
        /// </summary>
        public static V3d UnprojectPointOnNearPlane(this ICameraProjection self, Ndc2d p)
            => self.ProjectionTrafo.Backward.TransformPosProj(new V3d(p.Position, 0.0));

        /// <summary>
        /// Gets point on far plane (in camera space) from point in normalized device coordinates.
        /// </summary>
        public static V3d UnprojectPointOnFarPlane(this ICameraProjection self, Ndc2d p)
            => self.ProjectionTrafo.Backward.TransformPosProj(new V3d(p.Position, 1.0));

        /// <summary>
        /// Gets normalized device coordinates from point in camera space.
        /// </summary>
        public static Ndc3d TransformPos(this ICameraProjection self, V3d posInCameraSpace)
            => new Ndc3d(self.ProjectionTrafo.Forward.TransformPosProj(posInCameraSpace));

        /// <summary>
        /// Scales clipping window by given factor.
        /// </summary>
        public static void Zoom(this ICameraProjection self, V2d center, double factor)
        {
            if (factor <= 0.0) throw new ArgumentException("Factor needs to be greater than 0.0, but is " + factor + ".", "factor");
            var box = self.ClippingWindow;
            box.Min.X = (box.Min.X - center.X) * factor + center.X;
            box.Min.Y = (box.Min.Y - center.Y) * factor + center.Y;
            box.Max.X = (box.Max.X - center.X) * factor + center.X;
            box.Max.Y = (box.Max.Y - center.Y) * factor + center.Y;
            self.ClippingWindow = box;
        }
    }
}
