using System;

namespace Aardvark.Base
{
    public class CameraProjectionOrtho : ICameraProjection
    {
        private Trafo3d m_trafo;
        private EventSource<Trafo3d> m_trafoChanges = new EventSource<Trafo3d>();
        private Box3d m_box;
        
        public CameraProjectionOrtho(double left, double right, double bottom, double top, double near, double far)
        {
            SetClippingParams(left, right, bottom, top, near, far);
        }

        public Trafo3d ProjectionTrafo => m_trafo;

        public IEvent<Trafo3d> ProjectionTrafos => m_trafoChanges;

        public Ray3d Unproject(V2d xyOnNearPlane)
            => new Ray3d(new V3d(xyOnNearPlane, 0.0), new V3d(xyOnNearPlane, -m_box.Min.Z));

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

        public double Near
        {
            get { return m_box.Min.Z; }
            set
            {
                if (!(value > 0.0 && value < Far)) throw new ArgumentOutOfRangeException();
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
                if (m_box.IsInvalid) throw new ArgumentOutOfRangeException();
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
                var w = m_box.Max.X - m_box.Min.X;
                var hOld = m_box.Max.Y - m_box.Min.Y;
                var hNew = w / value;
                var f = hNew / hOld;
                var c = (m_box.Min.Y + m_box.Max.Y) * 0.5;
                m_box.Min.Y = (m_box.Min.Y - c) * f + c;
                m_box.Max.Y = (m_box.Max.Y - c) * f + c;
                UpdateProjectionTrafo();
            }
        }

        /// <summary>
        /// Scales clipping window by given factor.
        /// </summary>
        public void Zoom(V2d center, double factor)
        {
            if (factor <= 0.0) throw new ArgumentException("Factor needs to be greater than 0.0, but is " + factor + ".", "factor");
            m_box.Min.X = (m_box.Min.X - center.X) * factor + center.X;
            m_box.Min.Y = (m_box.Min.Y - center.Y) * factor + center.Y;
            m_box.Max.X = (m_box.Max.X - center.X) * factor + center.X;
            m_box.Max.Y = (m_box.Max.Y - center.Y) * factor + center.Y;
            UpdateProjectionTrafo();
        }

        private void UpdateProjectionTrafo()
        {
            var l = m_box.Min.X; var r = m_box.Max.X;
            var b = m_box.Min.Y; var t = m_box.Max.Y;
            var n = m_box.Min.Z; var f = m_box.Max.Z;

            m_trafo = new Trafo3d(
                new M44d(
                    2 / (r - l),               0,               0,     (l + r) / (l - r),
                              0,     2 / (t - b),               0,     (b + t) / (b - t),
                              0,               0,     1 / (n - f),           n / (n - f),
                              0,               0,               0,                     1
                    ),

                new M44d(
                    (r - l) / 2,               0,               0,           (l + r) / 2,
                              0,     (t - b) / 2,               0,           (b + t) / 2,
                              0,               0,           n - f,                    -n,
                              0,               0,               0,                     1
                    )
                );

            m_trafoChanges.Emit(m_trafo);
        }
    }
}
