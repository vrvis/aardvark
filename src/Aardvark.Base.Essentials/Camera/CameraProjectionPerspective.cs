using System;

namespace Aardvark.Base
{
    public class CameraProjectionPerspective : ICameraProjectionPerspective
    {
        private Trafo3d m_trafo;
        private EventSource<Trafo3d> m_trafoChanges = new EventSource<Trafo3d>();
        private Box3d m_box;

        public CameraProjectionPerspective(double horizontalFovInDegrees, double near, double far, double aspectRatio = 1.0)
        {
            SetClippingParams(horizontalFovInDegrees, near, far, aspectRatio);
        }

        public CameraProjectionPerspective(double left, double right, double bottom, double top, double near, double far)
        {
            SetClippingParams(left, right, bottom, top, near, far);
        }

        public Trafo3d ProjectionTrafo => m_trafo;

        public IEvent<Trafo3d> ProjectionTrafos => m_trafoChanges;

        public Ray3d Unproject(V2d xyOnNearPlane)
            => new Ray3d(V3d.Zero, new V3d(xyOnNearPlane, -m_box.Min.Z));

        public void SetClippingParams(double left, double right, double bottom, double top, double near, double far)
        {
            m_box.Min.X = left;
            m_box.Max.X = right;
            m_box.Min.Y = bottom;
            m_box.Max.Y = top;
            m_box.Min.Z = near;
            m_box.Max.Z = far;

            UpdateProjectionTrafo();
        }

        public void SetClippingParams(double horizontalFovInDegrees, double near, double far, double aspectRatio = 1.0)
        {
            var d = System.Math.Tan(Conversion.RadiansFromDegrees(horizontalFovInDegrees) * 0.5) * near;
            m_box.Min.X = -d;
            m_box.Max.X = +d;
            m_box.Min.Y = -d / aspectRatio;
            m_box.Max.Y = +d / aspectRatio;
            m_box.Min.Z = near;
            m_box.Max.Z = far;

            UpdateProjectionTrafo();
        }

        public double Near
        {
            get { return m_box.Min.Z; }
            set
            {
                if (!(value > 0.0 && value < Far)) throw new ArgumentOutOfRangeException();

                var s = value / m_box.Min.Z;
                m_box.Min.X *= s; m_box.Max.X *= s;
                m_box.Min.Y *= s; m_box.Max.Y *= s;
                m_box.Min.Z = value;
                UpdateProjectionTrafo();
            }
        }

        public double Far
        {
            get { return m_box.Max.Z; }
            set
            {
                if (!(value > 0.0 && value > Near)) throw new ArgumentOutOfRangeException();
                m_box.Max.Z = value;
                UpdateProjectionTrafo();
            }
        }

        public Box2d ClippingWindow
        {
            get { return m_box.XY; }
            set
            {
                if (m_box.IsInvalid) throw new ArgumentException();
                m_box.Min.X = value.Min.X;
                m_box.Min.Y = value.Min.Y;
                m_box.Max.X = value.Max.X;
                m_box.Max.Y = value.Max.Y;
                UpdateProjectionTrafo();
            }
        }

        public double AspectRatio
        {
            get
            {
                return (m_box.Max.X - m_box.Min.X) / (m_box.Max.Y - m_box.Min.Y);
            }
            set
            {
                if (value <= 0.0) throw new ArgumentOutOfRangeException();
                m_box.Min.Y = m_box.Min.X / value;
                m_box.Max.Y = m_box.Max.X / value;
                UpdateProjectionTrafo();
            }
        }

        public double HorizontalFieldOfViewInDegrees
        {
            get
            {
                var l = System.Math.Atan2(m_box.Min.X, m_box.Min.Z);
                var r = System.Math.Atan2(m_box.Max.X, m_box.Min.Z);
                return Conversion.DegreesFromRadians(-l + r);
            }
        }

        private void UpdateProjectionTrafo()
        {
            var l = m_box.Min.X; var r = m_box.Max.X;
            var b = m_box.Min.Y; var t = m_box.Max.Y;
            var n = m_box.Min.Z; var f = m_box.Max.Z;

            m_trafo = Trafo3d.PerspectiveProjectionRH(l, r, b, t, n, f);

            m_trafoChanges.Emit(m_trafo);
        }
    }
}
