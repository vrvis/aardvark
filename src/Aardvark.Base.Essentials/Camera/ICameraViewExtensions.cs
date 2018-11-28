namespace Aardvark.Base
{
    public static class ICameraViewExtensions
    {
        /// <summary>
        /// Turns camera to look at given point p.
        /// </summary>
        public static void LookAt(this ICameraView self, V3d p)
            => self.Forward = (p - self.Location).Normalized;
    }
}
