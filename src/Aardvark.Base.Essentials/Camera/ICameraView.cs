namespace Aardvark.Base
{
    public interface ICameraView
    {
        /// <summary>
        /// Gets or sets view trafo, which transforms world space into camera space.
        /// 
        /// In camera space the camera is placed at the origin looking
        /// down the negative z-axis, with the y-axis pointing upwards
        /// and the x-axis pointing to the right.
        /// </summary>
        Trafo3d ViewTrafo { get; set; }

        /// <summary>
        /// Changes of ViewTrafo.
        /// </summary>
        IEvent<Trafo3d> ViewTrafos { get; }

        /// <summary>
        /// Gets or sets camera location in world space (origin in camera space).
        /// </summary>
        V3d Location { get; set; }

        /// <summary>
        /// Gets or sets camera right vector in world space (+x in camera space).
        /// </summary>
        V3d Right { get; set; }

        /// <summary>
        /// Gets or sets camera up vector in world space (+y in camera space).
        /// </summary>
        V3d Up { get; set; }

        /// <summary>
        /// Gets or sets camera forward direction in world space (-z in camera space).
        /// </summary>
        V3d Forward { get; set; }

        /// <summary>
        /// Sets location and axes in a single transaction.
        /// </summary>
        void Set(V3d location, V3d right, V3d up, V3d forward);
    }
}
