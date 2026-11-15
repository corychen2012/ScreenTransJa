using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenTrans
{
    /// <summary>
    /// 识别区域的外轮廓框：
    /// - 只显示一圈细边（用 Region 挖空中间），不遮挡画面
    /// - WS_EX_TRANSPARENT 鼠标穿透，不影响点击/游戏操作
    /// - 不抢焦点、不出现在 Alt+Tab
    /// </summary>
    public class SelectionFrame : Form
    {
        public const int Thickness = 3;
        Color _color = Color.FromArgb(64, 190, 235);

        public SelectionFrame()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = _color;
            StartPosition = FormStartPosition.Manual;
            Width = 1;
            Height = 1;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                //cp.ExStyle |= 0x00000020; // 先关掉穿透测试绘制
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE：不抢焦点
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不在 Alt+Tab
                return cp;
            }
        }

        public void SetArea(Rectangle area)
        {
            if (area.Width < 8 || area.Height < 8) { Hide(); return; }

            Bounds = area;
            UpdateRegion();
            Invalidate(true);
            Update();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateRegion();
            Invalidate(true);
            Update();
        }

        protected override void WndProc(ref Message m)
        {
            // WM_NCHITTEST -> HTTRANSPARENT：鼠标事件穿透到下层窗口，
            // 不用 WS_EX_TRANSPARENT（那个样式在这里会让窗口画不出来）
            if (m.Msg == 0x0084)
            {
                m.Result = (IntPtr)(-1);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Region 已挖掉中间，这里再画一圈边框，保证一定可见
            int t = Thickness;
            using (Pen p = new Pen(_color, t))
            {
                p.Alignment = PenAlignment.Inset;
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }

        public void SetActive(bool active)
        {
            _color = active ? Color.FromArgb(64, 190, 235) : Color.FromArgb(120, 128, 138);
            BackColor = _color;
            UpdateRegion();
        }

        void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;

            int t = Thickness;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.FillMode = FillMode.Alternate;
                path.AddRectangle(new Rectangle(0, 0, Width, Height));
                if (Width > t * 2 && Height > t * 2)
                    path.AddRectangle(new Rectangle(t, t, Width - t * 2, Height - t * 2));
                Region = new Region(path);
            }
        }
    }
}
