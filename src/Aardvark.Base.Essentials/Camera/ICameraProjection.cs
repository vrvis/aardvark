namespace Aardvark.Base
{
    public interface ICameraProjection
    {
        /// <summary>
        /// Transforms the right-handed camera space (looking down the negative z-axis)
        /// to left-handed normalized device coordinates of the canonical view volume
        /// defined as [(-1, -1, 0), (+1, +1, +1)].
        /// Here the x-axis goes to the right, the y-axis goes up, and the z-axis goes
        /// into the viewport, with near plane mapped to z = 0 and far plane to z = 1.
        /// </summary>
        Trafo3d ProjectionTrafo { get; }

        /// <summary>
        /// Changes of ProjectionTrafo.
        /// </summary>
        IEvent<Trafo3d> ProjectionTrafos { get; }

        /// <summary>
        /// E.g. SetClippingParams(-1, +1, -0.75, +0.75, 1.0, 1000.0)
        /// will create 90° horizontal field-of-view, with aspect ratio 4:3,
        /// near distance at 1.0, and far distance at 1000.0.
        /// </summary>
        void SetClippingParams(double left, double right, double bottom, double top, double near, double far);

        /// <summary>
        /// Gets or sets distance of near clipping plane.
        /// Same as 'near' argument in SetClippingParams.
        /// </summary>
        double Near { get; set; }

        /// <summary>
        /// Gets or sets distance of far clipping plane.
        /// Same as 'far' argument in SetClippingParams.
        /// </summary>
        double Far { get; set; }

        /// <summary>
        /// Gets or sets clipping window on near plane [(left, bottom), (right, top)].
        /// Same as 'left', 'right', 'bottom', 'top' arguments in SetClippingParams.
        /// </summary>
        Box2d ClippingWindow { get; set; }

        /// <summary>
        /// Width / Height.
        /// </summary>
        double AspectRatio { get; set; }
    }

    public interface ICameraProjectionPerspective : ICameraProjection
    {
        double HorizontalFieldOfViewInDegrees { get; }
    }
}
