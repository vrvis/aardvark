namespace Aardvark.Base
{
    public interface IViewProjection
    {
        /// <summary>
        /// The view transformation
        /// </summary>
        ICameraView View { get; }

        /// <summary>
        /// The projection transformation
        /// </summary>
        ICameraProjection Projection { get; }
    }

    public interface IRenderView: IViewProjection
    {
        /// <summary>
        /// Region of the RenderBuffer or Control
        /// </summary>
        Box2i Region { get; }
    }
}
