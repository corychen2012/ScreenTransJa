using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenTrans
{
    /// <summary>
    /// 全屏选区层：先把整屏截成位图显示出来（画面冻结），再让用户在上面拖拽框选。
    /// 不依赖透明窗口，Win7 上也能正常显示。
    /// </summary>
    public class OverlayForm : Form
    {
        readonly Bitmap _screen;
        Rectangle _sel = Rectangle.Empty;
        Point _start;
        bool _dragging;

        /// <summary>返回的选区（屏幕坐标）</summary>
        public Rectangle Selection { get; private set; }

        public OverlayForm(Rectangle virtualBounds, Bitmap screen)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Text = "选择翻译区域";
            _screen = screen;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            if (_screen != null)
                g.DrawImageUnscaled(_screen, 0, 0);

            // 选区以外压暗，突出选区
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                if (_sel.Width <= 0 || _sel.Height <= 0)
                {
                    g.FillRectangle(dim, ClientRectangle);
                }
                else
                {
                    g.FillRectangle(dim, 0, 0, Width, _sel.Top);
                    g.FillRectangle(dim, 0, _sel.Bottom, Width, Height - _sel.Bottom);
                    g.FillRectangle(dim, 0, _sel.Top, _sel.Left, _sel.Height);
                    g.FillRectangle(dim, _sel.Right, _sel.Top, Width - _sel.Right, _sel.Height);
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                using (Pen pen = new Pen(Color.FromArgb(80, 195, 255), 2f))
                    g.DrawRectangle(pen, _sel);

                using (Pen pen2 = new Pen(Color.White, 1f))
                {
                    pen2.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen2, Rectangle.Inflate(_sel, 2, 2));
                }

                using (SolidBrush b = new SolidBrush(Color.FromArgb(80, 195, 255)))
                {
                    const int s = 6;
                    g.FillRectangle(b, _sel.Left - 1, _sel.Top - 1, s, s);
                    g.FillRectangle(b, _sel.Right - s + 1, _sel.Top - 1, s, s);
                    g.FillRectangle(b, _sel.Left - 1, _sel.Bottom - s + 1, s, s);
                    g.FillRectangle(b, _sel.Right - s + 1, _sel.Bottom - s + 1, s, s);
                }

                string info = _sel.Width + " × " + _sel.Height;
                using (Font font = new Font("Microsoft YaHei", 10f, FontStyle.Bold))
                using (SolidBrush white = new SolidBrush(Color.White))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(210, 18, 18, 24)))
                {
                    SizeF sz = g.MeasureString(info, font);
                    RectangleF r = new RectangleF(_sel.Left, _sel.Top - sz.Height - 8, sz.Width + 16, sz.Height + 6);
                    if (r.Top < 0) r.Y = _sel.Top + 6;
                    g.FillRectangle(bg, r);
                    g.DrawString(info, font, white, r.X + 8, r.Y + 3);
                }
            }
            else
            {
                DrawHint(g);
            }
        }

        void DrawHint(Graphics g)
        {
            string title = "拖动鼠标框选要翻译的日文区域";
            string sub = "松开即识别翻译    ·    Esc 取消";

            using (Font f1 = new Font("Microsoft YaHei", 20f, FontStyle.Bold))
            using (Font f2 = new Font("Microsoft YaHei", 12f))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush grey = new SolidBrush(Color.FromArgb(210, 210, 210)))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(225, 16, 16, 20)))
            using (Pen border = new Pen(Color.FromArgb(80, 195, 255), 1f))
            {
                SizeF s1 = g.MeasureString(title, f1);
                SizeF s2 = g.MeasureString(sub, f2);
                float cx = Width / 2f;
                float cy = Height / 2f;

                float bw = Math.Max(s1.Width, s2.Width) + 64;
                float bh = s1.Height + s2.Height + 52;
                RectangleF box = new RectangleF(cx - bw / 2f, cy - bh / 2f, bw, bh);
                g.FillRectangle(bg, box);
                g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);

                g.DrawString(title, f1, white, cx - s1.Width / 2f, box.Y + 16);
                g.DrawString(sub, f2, grey, cx - s2.Width / 2f, box.Y + 16 + s1.Height + 10);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _sel = Rectangle.Empty;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            int x = Math.Min(_start.X, e.X);
            int y = Math.Min(_start.Y, e.Y);
            int w = Math.Abs(e.X - _start.X);
            int h = Math.Abs(e.Y - _start.Y);
            _sel = new Rectangle(x, y, w, h);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;

            if (_sel.Width >= 24 && _sel.Height >= 16)
            {
                Selection = new Rectangle(Left + _sel.Left, Top + _sel.Top, _sel.Width, _sel.Height);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _sel = Rectangle.Empty;
                Invalidate();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _screen != null) _screen.Dispose();
            base.Dispose(disposing);
        }
    }
}
