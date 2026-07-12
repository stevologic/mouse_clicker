using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClickForge
{
    // A real-time animated backdrop rendered entirely in GDI+: a moving aurora
    // gradient, a drifting particle constellation (nodes linked by lines that
    // react to the cursor), and expanding click-pulse rings. One frame is
    // rendered to a shared bitmap each tick; the form and every glass panel
    // blit the region they need, so the whole UI shares one animated scene.
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

        private Bitmap _frame;
        private Bitmap _low;      // quarter-res buffer the aurora is painted into
        private Bitmap _bg;       // full-size upscaled aurora, refreshed occasionally
        private int _w, _h, _lw, _lh;
        private int _bgAge;
        private const int Down = 4;
        private const int BgEvery = 4; // refresh the upscaled aurora every N frames
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly Random _rng = new Random();
        private double _phase;
        private PointF _cursor;
        private bool _cursorInside;

        // Aurora blob definitions (color + orbit parameters).
        private struct Blob { public Color Color; public float Cx, Cy, Ax, Ay, Sx, Sy, R; }
        private readonly List<Blob> _blobs = new List<Blob>();

        private const float LinkDist = 132f;
        private const float CursorRadius = 150f;

        public Bitmap Frame { get { return _frame; } }

        public void Resize(int w, int h)
        {
            if (w < 2) w = 2;
            if (h < 2) h = 2;
            _w = w; _h = h;
            _lw = Math.Max(1, w / Down);
            _lh = Math.Max(1, h / Down);
            if (_frame != null) _frame.Dispose();
            _frame = new Bitmap(w, h);
            if (_low != null) _low.Dispose();
            _low = new Bitmap(_lw, _lh);
            if (_bg != null) _bg.Dispose();
            _bg = new Bitmap(w, h);
            _bgAge = 0;

            // Particle density scaled to area, kept in a sane range.
            int count = (int)(w * h / 12000.0);
            if (count < 36) count = 36;
            if (count > 90) count = 90;

            _particles.Clear();
            for (int i = 0; i < count; i++)
                _particles.Add(NewParticle());

            _blobs.Clear();
            AddBlob(Color.FromArgb(99, 130, 255), 0.30f, 0.20f, 0.22f, 0.16f, 0.11f, 0.07f, 0.42f);
            AddBlob(Color.FromArgb(150, 110, 255), 0.72f, 0.30f, 0.20f, 0.18f, 0.08f, 0.13f, 0.40f);
            AddBlob(Color.FromArgb(64, 176, 220), 0.55f, 0.78f, 0.24f, 0.14f, 0.14f, 0.09f, 0.38f);
            AddBlob(Color.FromArgb(120, 90, 230), 0.14f, 0.70f, 0.16f, 0.16f, 0.10f, 0.12f, 0.34f);
        }

        private void AddBlob(Color c, float cx, float cy, float ax, float ay, float sx, float sy, float r)
        {
            Blob b;
            b.Color = c; b.Cx = cx; b.Cy = cy; b.Ax = ax; b.Ay = ay; b.Sx = sx; b.Sy = sy; b.R = r;
            _blobs.Add(b);
        }

        private Particle NewParticle()
        {
            Particle p = new Particle();
            p.X = (float)(_rng.NextDouble() * _w);
            p.Y = (float)(_rng.NextDouble() * _h);
            double a = _rng.NextDouble() * Math.PI * 2;
            float speed = 0.10f + (float)_rng.NextDouble() * 0.18f;
            p.VX = (float)Math.Cos(a) * speed;
            p.VY = (float)Math.Sin(a) * speed;
            p.R = 1.3f + (float)_rng.NextDouble() * 1.8f;
            p.Twinkle = (float)_rng.NextDouble();
            p.TwinkleSpeed = 0.01f + (float)_rng.NextDouble() * 0.02f;
            return p;
        }

        public void SetCursor(float x, float y, bool inside)
        {
            _cursor = new PointF(x, y);
            _cursorInside = inside;
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
            _phase += 0.016;

            for (int i = 0; i < _particles.Count; i++)
            {
                Particle p = _particles[i];

                // Cursor repulsion — a soft push outward within a radius.
                if (_cursorInside)
                {
                    float dx = p.X - _cursor.X;
                    float dy = p.Y - _cursor.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < CursorRadius * CursorRadius && d2 > 0.01f)
                    {
                        float d = (float)Math.Sqrt(d2);
                        float force = (1f - d / CursorRadius) * 0.6f;
                        p.VX += (dx / d) * force;
                        p.VY += (dy / d) * force;
                    }
                }

                p.X += p.VX;
                p.Y += p.VY;

                // Gentle damping so repelled particles settle back to a drift.
                p.VX *= 0.96f;
                p.VY *= 0.96f;
                float sp = (float)Math.Sqrt(p.VX * p.VX + p.VY * p.VY);
                float minSp = 0.10f;
                if (sp < minSp && sp > 0.0001f)
                {
                    p.VX = p.VX / sp * minSp;
                    p.VY = p.VY / sp * minSp;
                }

                // Wrap around the edges with a small margin.
                float m = 20f;
                if (p.X < -m) p.X = _w + m;
                if (p.X > _w + m) p.X = -m;
                if (p.Y < -m) p.Y = _h + m;
                if (p.Y > _h + m) p.Y = -m;

                p.Twinkle += p.TwinkleSpeed;
            }

            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                _ripples[i].Age += 1f;
                if (_ripples[i].Age >= _ripples[i].Life)
                    _ripples.RemoveAt(i);
            }
        }

        public void Render()
        {
            if (_frame == null || _low == null || _bg == null) return;

            // The aurora is expensive per pixel and moves slowly, so refresh the
            // full-size upscaled background only every few frames. Each frame we
            // just blit that cached background and redraw the constellation on
            // top — the moving part — which keeps per-frame cost low.
            if (_bgAge % BgEvery == 0)
            {
                using (Graphics g2 = Graphics.FromImage(_low))
                {
                    g2.SmoothingMode = SmoothingMode.None;
                    RenderBase(g2);
                }
                using (Graphics gb = Graphics.FromImage(_bg))
                {
                    gb.InterpolationMode = InterpolationMode.Bilinear;
                    gb.PixelOffsetMode = PixelOffsetMode.Half;
                    gb.DrawImage(_low, new Rectangle(0, 0, _w, _h));
                }
            }
            _bgAge++;

            using (Graphics g = Graphics.FromImage(_frame))
            {
                g.DrawImageUnscaled(_bg, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawConstellation(g);
                DrawRipples(g);
            }
        }

        // Base gradient + aurora blobs, drawn at low resolution.
        private void RenderBase(Graphics g)
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Rectangle(0, 0, _lw, _lh),
                Color.FromArgb(13, 15, 22), Color.FromArgb(9, 10, 16), 90f))
                g.FillRectangle(bg, 0, 0, _lw, _lh);

            for (int i = 0; i < _blobs.Count; i++)
            {
                Blob b = _blobs[i];
                float cx = (b.Cx + (float)Math.Sin(_phase * b.Sx + i) * b.Ax) * _lw;
                float cy = (b.Cy + (float)Math.Cos(_phase * b.Sy + i * 1.7) * b.Ay) * _lh;
                float rad = b.R * Math.Max(_lw, _lh)
                            * (0.9f + 0.1f * (float)Math.Sin(_phase * 0.6 + i));

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                    using (PathGradientBrush pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.FromArgb(52, b.Color);
                        pgb.SurroundColors = new Color[] { Color.FromArgb(0, b.Color) };
                        pgb.CenterPoint = new PointF(cx, cy);
                        g.FillPath(pgb, path);
                    }
                }
            }
        }

        private void DrawConstellation(Graphics g)
        {
            int n = _particles.Count;

            // Links between nearby particles.
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
                    int alpha = (int)((1f - d / LinkDist) * 70f);
                    if (alpha < 4) continue;
                    using (Pen pen = new Pen(Color.FromArgb(alpha, 120, 145, 225), 1f))
                        g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
                }
            }

            // Links from cursor to nearby particles + a soft cursor glow.
            if (_cursorInside)
            {
                for (int i = 0; i < n; i++)
                {
                    Particle a = _particles[i];
                    float dx = a.X - _cursor.X, dy = a.Y - _cursor.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > CursorRadius * CursorRadius) continue;
                    float d = (float)Math.Sqrt(d2);
                    int alpha = (int)((1f - d / CursorRadius) * 120f);
                    if (alpha < 4) continue;
                    using (Pen pen = new Pen(Color.FromArgb(alpha, 150, 175, 255), 1.1f))
                        g.DrawLine(pen, a.X, a.Y, _cursor.X, _cursor.Y);
                }
                DrawGlow(g, _cursor.X, _cursor.Y, 26f, Color.FromArgb(60, 150, 175, 255));
            }

            // Particle nodes: a cheap translucent halo + a bright core. Solid
            // brushes only — no per-particle gradient (keeps the loop light).
            for (int i = 0; i < n; i++)
            {
                Particle p = _particles[i];
                float tw = 0.55f + 0.45f * (float)Math.Sin(p.Twinkle);
                float hr = p.R * 2.3f;
                using (SolidBrush halo = new SolidBrush(Color.FromArgb((int)(34 * tw), 150, 175, 255)))
                    g.FillEllipse(halo, p.X - hr, p.Y - hr, hr * 2, hr * 2);
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(210 * tw), 205, 216, 255)))
                    g.FillEllipse(b, p.X - p.R, p.Y - p.R, p.R * 2, p.R * 2);
            }
        }

        private void DrawRipples(Graphics g)
        {
            for (int i = 0; i < _ripples.Count; i++)
            {
                Ripple r = _ripples[i];
                float t = r.Age / r.Life;
                float rad = r.MaxR * EaseOut(t);
                int alpha = (int)((1f - t) * 150f);
                if (alpha < 2) continue;
                float width = 2.4f * (1f - t) + 0.4f;
                using (Pen pen = new Pen(Color.FromArgb(alpha, r.Color), width))
                    g.DrawEllipse(pen, r.X - rad, r.Y - rad, rad * 2, rad * 2);
            }
        }

        private static void DrawGlow(Graphics g, float x, float y, float radius, Color color)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x - radius, y - radius, radius * 2, radius * 2);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = color;
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillPath(pgb, path);
                }
            }
        }

        private static float EaseOut(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }
    }
}
