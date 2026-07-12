using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClickForge
{
    // Animated backdrop rendered in GDI+. The aurora + base gradient are baked
    // ONCE at full resolution into a crisp static bitmap (so it never looks
    // upscaled and never crawls); only the lightweight particle constellation
    // is redrawn each frame on top. That keeps every frame cheap, so the UI
    // stays smooth and responsive.
    internal class Scene
    {
        private class Particle
        {
            public float X, Y, VX, VY, R, Twinkle, TwinkleSpeed;
        }

        private class Ripple
        {
            public float X, Y, Age, Life, MaxR;
            public Color Color;
        }

        private Bitmap _frame;   // per-frame composite (bg + constellation)
        private Bitmap _bg;      // static, full-res baked background
        private int _w, _h;
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly Random _rng = new Random();
        private PointF _cursor;
        private bool _cursorInside;
        private Rectangle[] _clipRects;   // only draw particles where they're visible

        private struct Blob { public Color Color; public float Cx, Cy, R; }
        private readonly Blob[] _blobs;

        private const float LinkDist = 128f;
        private const float CursorRadius = 150f;

        public Bitmap Frame { get { return _frame; } }

        public Scene()
        {
            _blobs = new Blob[]
            {
                MakeBlob(Color.FromArgb(99, 130, 255), 0.26f, 0.16f, 0.46f),
                MakeBlob(Color.FromArgb(150, 110, 255), 0.74f, 0.24f, 0.44f),
                MakeBlob(Color.FromArgb(64, 176, 220), 0.55f, 0.82f, 0.42f),
                MakeBlob(Color.FromArgb(120, 90, 230), 0.12f, 0.72f, 0.38f),
                MakeBlob(Color.FromArgb(88, 120, 240), 0.90f, 0.70f, 0.34f)
            };
        }

        private static Blob MakeBlob(Color c, float cx, float cy, float r)
        {
            Blob b; b.Color = c; b.Cx = cx; b.Cy = cy; b.R = r; return b;
        }

        public void Resize(int w, int h)
        {
            if (w < 2) w = 2;
            if (h < 2) h = 2;
            _w = w; _h = h;
            if (_frame != null) _frame.Dispose();
            _frame = new Bitmap(w, h);
            if (_bg != null) _bg.Dispose();
            _bg = new Bitmap(w, h);

            int count = (int)(w * h / 20000.0);
            if (count < 24) count = 24;
            if (count > 46) count = 46;
            _particles.Clear();
            for (int i = 0; i < count; i++)
                _particles.Add(NewParticle());

            BakeBackground();
        }

        private Particle NewParticle()
        {
            Particle p = new Particle();
            p.X = (float)(_rng.NextDouble() * _w);
            p.Y = (float)(_rng.NextDouble() * _h);
            double a = _rng.NextDouble() * Math.PI * 2;
            float speed = 0.12f + (float)_rng.NextDouble() * 0.16f;
            p.VX = (float)Math.Cos(a) * speed;
            p.VY = (float)Math.Sin(a) * speed;
            p.R = 1.3f + (float)_rng.NextDouble() * 1.7f;
            p.Twinkle = (float)_rng.NextDouble();
            p.TwinkleSpeed = 0.02f + (float)_rng.NextDouble() * 0.03f;
            return p;
        }

        // Full-resolution aurora, rendered once. Crisp, no upscaling.
        private void BakeBackground()
        {
            using (Graphics g = Graphics.FromImage(_bg))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, _w, _h),
                    Color.FromArgb(14, 16, 24), Color.FromArgb(9, 10, 16), 90f))
                    g.FillRectangle(bg, 0, 0, _w, _h);

                int maxd = Math.Max(_w, _h);
                foreach (Blob b in _blobs)
                {
                    float cx = b.Cx * _w, cy = b.Cy * _h, rad = b.R * maxd;
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                        using (PathGradientBrush pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(60, b.Color);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, b.Color) };
                            pgb.CenterPoint = new PointF(cx, cy);
                            g.FillPath(pgb, path);
                        }
                    }
                }
            }
        }

        public void SetCursor(float x, float y, bool inside)
        {
            _cursor = new PointF(x, y);
            _cursorInside = inside;
        }

        // The regions where the frame is actually shown (the glass panels), so
        // the constellation isn't drawn across the large hidden content area.
        public void SetVisibleRects(Rectangle[] rects)
        {
            _clipRects = rects;
        }

        public void AddRipple(float x, float y, Color color, float maxR)
        {
            if (_ripples.Count > 40) return;
            Ripple r = new Ripple();
            r.X = x; r.Y = y; r.Age = 0; r.Life = 46; r.MaxR = maxR; r.Color = color;
            _ripples.Add(r);
        }

        public void Update()
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                Particle p = _particles[i];
                if (_cursorInside)
                {
                    float dx = p.X - _cursor.X, dy = p.Y - _cursor.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < CursorRadius * CursorRadius && d2 > 0.01f)
                    {
                        float d = (float)Math.Sqrt(d2);
                        float force = (1f - d / CursorRadius) * 0.5f;
                        p.VX += (dx / d) * force;
                        p.VY += (dy / d) * force;
                    }
                }
                p.X += p.VX; p.Y += p.VY;
                p.VX *= 0.96f; p.VY *= 0.96f;
                float sp = (float)Math.Sqrt(p.VX * p.VX + p.VY * p.VY);
                if (sp < 0.12f && sp > 0.0001f) { p.VX = p.VX / sp * 0.12f; p.VY = p.VY / sp * 0.12f; }
                float m = 20f;
                if (p.X < -m) p.X = _w + m; if (p.X > _w + m) p.X = -m;
                if (p.Y < -m) p.Y = _h + m; if (p.Y > _h + m) p.Y = -m;
                p.Twinkle += p.TwinkleSpeed;
            }
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                _ripples[i].Age += 1f;
                if (_ripples[i].Age >= _ripples[i].Life) _ripples.RemoveAt(i);
            }
        }

        public void Render()
        {
            if (_frame == null || _bg == null) return;
            using (Graphics g = Graphics.FromImage(_frame))
            {
                g.DrawImageUnscaled(_bg, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawConstellation(g);
                DrawRipples(g);
            }
        }

        private void DrawConstellation(Graphics g)
        {
            int n = _particles.Count;
            for (int i = 0; i < n; i++)
            {
                Particle a = _particles[i];
                for (int j = i + 1; j < n; j++)
                {
                    Particle b = _particles[j];
                    float dx = a.X - b.X, dy = a.Y - b.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > LinkDist * LinkDist) continue;
                    float d = (float)Math.Sqrt(d2);
                    int alpha = (int)((1f - d / LinkDist) * 66f);
                    if (alpha < 4) continue;
                    using (Pen pen = new Pen(Color.FromArgb(alpha, 120, 145, 225), 1f))
                        g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
                }
            }

            if (_cursorInside)
            {
                for (int i = 0; i < n; i++)
                {
                    Particle a = _particles[i];
                    float dx = a.X - _cursor.X, dy = a.Y - _cursor.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > CursorRadius * CursorRadius) continue;
                    float d = (float)Math.Sqrt(d2);
                    int alpha = (int)((1f - d / CursorRadius) * 110f);
                    if (alpha < 4) continue;
                    using (Pen pen = new Pen(Color.FromArgb(alpha, 150, 175, 255), 1.1f))
                        g.DrawLine(pen, a.X, a.Y, _cursor.X, _cursor.Y);
                }
            }

            for (int i = 0; i < n; i++)
            {
                Particle p = _particles[i];
                float tw = 0.55f + 0.45f * (float)Math.Sin(p.Twinkle);
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(215 * tw), 205, 216, 255)))
                    g.FillEllipse(b, p.X - p.R, p.Y - p.R, p.R * 2, p.R * 2);
            }
        }

        private void DrawRipples(Graphics g)
        {
            for (int i = 0; i < _ripples.Count; i++)
            {
                Ripple r = _ripples[i];
                float t = r.Age / r.Life;
                float rad = r.MaxR * (1f - (1f - t) * (1f - t));
                int alpha = (int)((1f - t) * 150f);
                if (alpha < 2) continue;
                using (Pen pen = new Pen(Color.FromArgb(alpha, r.Color), 2.4f * (1f - t) + 0.4f))
                    g.DrawEllipse(pen, r.X - rad, r.Y - rad, rad * 2, rad * 2);
            }
        }
    }
}
