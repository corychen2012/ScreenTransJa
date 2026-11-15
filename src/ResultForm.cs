using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenTrans
{
    /// <summary>
    /// 悬浮译文窗：
    /// - 右下角拖拽改变大小
    /// - 译文用 RichTextBox，可选中/复制
    /// - 底栏可选模型、调字号，选完立刻重译
    /// - 顶栏可暂停/继续自动跟随
    /// </summary>
    public class ResultForm : Form
    {
        readonly Label _title = new Label();
        readonly Label _btnPause = new Label();
        readonly Label _btnCopy = new Label();
        readonly Label _btnRetry = new Label();
        readonly Label _btnMore = new Label();
        readonly Label _btnClose = new Label();
        readonly Label _btnHideAll = new Label();
        readonly RichTextBox _orig = new RichTextBox();
        readonly RichTextBox _rich = new RichTextBox();
        readonly ComboBox _models = new ComboBox();
        readonly Label _btnFontMinus = new Label();
        readonly Label _btnFontPlus = new Label();
        readonly Label _btnReselect = new Label();
        readonly Label _origToggle = new Label();
        readonly Label _dot = new Label();
        readonly System.Windows.Forms.Timer _pulse = new System.Windows.Forms.Timer();
        readonly Label _footer = new Label();

        readonly Font _origFont = new Font("Microsoft YaHei", 9.5f);
        readonly Font _footerFont = new Font("Microsoft YaHei", 9f);
        readonly Font _titleFont = new Font("Microsoft YaHei", 10f, FontStyle.Bold);

        const int Pad = 14;
        const int HeaderH = 28;
        const int FooterH = 34;
        const int GripSize = 18;

        int _fontSize = 20;
        string _hotkey = "F8";
        float _phase;
        bool _loading;
        float _progress;
        readonly System.Windows.Forms.Timer _loadTimer = new System.Windows.Forms.Timer();
        int _origH;
        bool _manualSize;
        bool _autoOn;
        bool _resizing;
        Point _resizeStartScreen;
        Size _resizeStartSize;

        string _zh = "";
        string _ja = "";
        bool _origExpanded;
        public event EventHandler OriginalToggled;

        public event EventHandler RetryRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler PauseRequested;
        public event EventHandler ModelChanged;
        public event EventHandler FontSizeChanged;
        public event EventHandler SizeChanged;
        public event EventHandler ReselectRequested;
        public event EventHandler HideAllRequested;

        public int FontSize { get { return _fontSize; } }
        public string SelectedModel { get { return Convert.ToString(_models.Text).Trim(); } }
        public bool OriginalExpanded { get { return _origExpanded; } }

        public ResultForm(AppConfig cfg)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(30, 31, 38);
            _fontSize = Math.Max(10, Math.Min(48, cfg.FontSize));
            _hotkey = cfg.Hotkey;

            Width = Math.Max(470, cfg.BoxWidth);
            Height = 200;
            if (cfg.BoxHeight > 120)
            {
                Height = cfg.BoxHeight;
                _manualSize = true;
            }

            BuildChild(cfg);
            ShowIdleHint();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
                return cp;
            }
        }

        // ---------------- 控件 ----------------
        void BuildChild(AppConfig cfg)
        {
            _origExpanded = cfg.OriginalExpanded;

            _title.Text = "译文";
            _title.Font = _titleFont;
            _title.ForeColor = Color.FromArgb(120, 200, 255);
            _title.BackColor = Color.Transparent;

            StyleButton(_btnPause, "暂停 F9");
            StyleButton(_btnCopy, "复制");
            StyleButton(_btnRetry, "重译 F10");
            StyleButton(_btnMore, "设置");
            StyleButton(_btnClose, "×");
            StyleButton(_btnHideAll, "隐藏");
            StyleButton(_btnFontMinus, "A-");
            StyleButton(_btnFontPlus, "A+");
            StyleButton(_btnReselect, "重选");

            _btnPause.ForeColor = Color.FromArgb(255, 190, 90);
            _btnFontMinus.Font = new Font("Microsoft YaHei", 9f, FontStyle.Bold);
            _btnFontPlus.Font = new Font("Microsoft YaHei", 9f, FontStyle.Bold);

            _btnPause.Click += delegate { if (PauseRequested != null) PauseRequested(this, EventArgs.Empty); };
            _btnCopy.Click += delegate { CopyText(); };
            _btnRetry.Click += delegate { if (RetryRequested != null) RetryRequested(this, EventArgs.Empty); };
            _btnMore.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            _btnClose.Click += delegate { Hide(); };
            _btnHideAll.Click += delegate { if (HideAllRequested != null) HideAllRequested(this, EventArgs.Empty); };
            _btnFontMinus.Click += delegate { ChangeFontSize(-2); };
            _btnFontPlus.Click += delegate { ChangeFontSize(2); };
            _btnReselect.Click += delegate { if (ReselectRequested != null) ReselectRequested(this, EventArgs.Empty); };

            // 自动跟随状态灯：呼吸动画
            _dot.BackColor = Color.Transparent;
            _dot.Paint += DotPaint;
            _pulse.Interval = 60;
            _pulse.Tick += delegate
            {
                _phase += 0.16f;
                if (_phase > 6.283f) _phase -= 6.283f;
                _dot.Invalidate();
            };
            _pulse.Start();

            _loadTimer.Interval = 50;
            _loadTimer.Tick += delegate
            {
                _progress += 0.014f;
                if (_progress > 1f) _progress = 0f;
                Invalidate();
            };

            _orig.ReadOnly = true;
            _orig.BorderStyle = BorderStyle.None;
            _orig.Font = _origFont;
            _orig.ForeColor = Color.FromArgb(150, 156, 170);
            _orig.BackColor = Color.FromArgb(30, 31, 38);
            _orig.ScrollBars = RichTextBoxScrollBars.None;
            _orig.DetectUrls = false;
            _orig.ShortcutsEnabled = true;
            _orig.WordWrap = true;

            _origToggle.Text = "▶ 原文";
            _origToggle.Font = new Font("Microsoft YaHei", 9f);
            _origToggle.ForeColor = Color.FromArgb(145, 152, 166);
            _origToggle.BackColor = Color.Transparent;
            _origToggle.Cursor = Cursors.Hand;
            _origToggle.TextAlign = ContentAlignment.MiddleLeft;
            _origToggle.Click += delegate { ToggleOriginal(); };

            _rich.ReadOnly = true;
            _rich.BorderStyle = BorderStyle.None;
            _rich.BackColor = Color.FromArgb(30, 31, 38);
            _rich.ForeColor = Color.White;
            _rich.Font = new Font("Microsoft YaHei", _fontSize, FontStyle.Regular);
            _rich.ScrollBars = RichTextBoxScrollBars.Vertical;
            _rich.DetectUrls = false;
            _rich.ShortcutsEnabled = true;
            _rich.WordWrap = true;

            _models.DropDownStyle = ComboBoxStyle.DropDown;
            _models.FlatStyle = FlatStyle.Flat;
            _models.Font = new Font("Microsoft YaHei", 9f);
            _models.BackColor = Color.FromArgb(42, 44, 54);
            _models.ForeColor = Color.White;
            foreach (string m in ModelsFor(cfg.ApiBase)) _models.Items.Add(m);
            // 双模型时显示识别模型，与设置里选的一致
            string showModel = (cfg.SplitModel && !string.IsNullOrWhiteSpace(cfg.OcrModel)) ? cfg.OcrModel : cfg.Model;
            _models.Text = showModel;
            _models.SelectedIndexChanged += delegate
            {
                string m = SelectedModel;
                if (m.Length == 0 || m == cfg.Model) return;
                if (ModelChanged != null) ModelChanged(this, EventArgs.Empty);
            };
            _models.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && ModelChanged != null) ModelChanged(this, EventArgs.Empty);
            };

            _footer.Font = _footerFont;
            _footer.ForeColor = Color.FromArgb(120, 126, 140);
            _footer.BackColor = Color.Transparent;
            _footer.TextAlign = ContentAlignment.MiddleLeft;

            Controls.AddRange(new Control[] {
                _title, _dot, _btnPause, _btnCopy, _btnRetry, _btnHideAll, _btnMore, _btnClose,
                _rich, _origToggle, _orig, _models, _btnReselect, _btnFontMinus, _btnFontPlus, _footer
            });

            // 只有标题/原文/底栏能拖动窗口，避免影响选中文字和下拉框
            _title.MouseDown += DragDown;
            _title.MouseMove += DragMove;
            _title.MouseUp += DragUp;
            
            _footer.MouseDown += DragDown;
            _footer.MouseMove += DragMove;
            _footer.MouseUp += DragUp;
            MouseDown += DragDown;
            MouseMove += DragMove;
            MouseUp += DragUp;
        }

        static void StyleButton(Label b, string text)
        {
            b.Text = text;
            b.Font = new Font("Microsoft YaHei", 9f);
            b.ForeColor = Color.FromArgb(190, 196, 210);
            b.BackColor = Color.Transparent;
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { b.ForeColor = Color.White; };
            b.MouseLeave += delegate { b.ForeColor = Color.FromArgb(190, 196, 210); };
        }

        static string[] ModelsFor(string baseUrl)
        {
            string b = (baseUrl ?? "").ToLowerInvariant();
            if (b.Contains("dashscope"))
                return new string[] { "qwen3-vl-plus", "qwen3.5-omni-plus", "qwen3.5-omni-flash", "qwen-vl-ocr" };
            if (b.Contains("bigmodel"))
                return new string[] { "glm-4v-flash", "glm-4.5v", "glm-4v-plus" };
            if (b.Contains("openai"))
                return new string[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini" };
            return new string[] { "qwen3-vl-plus", "glm-4v-flash", "gpt-4o-mini" };
        }

        // ---------------- 拖动 ----------------
        void DragDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.X >= Width - GripSize && e.Y >= Height - GripSize) return; // 右下角留给缩放
            _resizing = false;
            _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            _drag = true;
        }

        void DragMove(object sender, MouseEventArgs e)
        {
            if (!_drag) return;
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
        }

        void DragUp(object sender, MouseEventArgs e)
        {
            _drag = false;
        }

        bool _drag;
        Point _dragOffset;

        // ---------------- 右下角缩放 ----------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.X >= Width - GripSize && e.Y >= Height - GripSize)
            {
                _resizing = true;
                _drag = false;
                _resizeStartScreen = PointToScreen(e.Location);
                _resizeStartSize = Size;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_resizing)
            {
                Point p = PointToScreen(e.Location);
                int w = _resizeStartSize.Width + (p.X - _resizeStartScreen.X);
                int h = _resizeStartSize.Height + (p.Y - _resizeStartScreen.Y);
                Width = Math.Max(470, w);
                Height = Math.Max(180, h);
                _manualSize = true;
                LayoutAll();
                Invalidate();
                return;
            }
            Cursor = (e.X >= Width - GripSize && e.Y >= Height - GripSize) ? Cursors.SizeNWSE : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_resizing)
            {
                _resizing = false;
                if (SizeChanged != null) SizeChanged(this, EventArgs.Empty);
            }
        }

        // ---------------- 文本与布局 ----------------
        void ChangeFontSize(int delta)
        {
            int size = Math.Max(10, Math.Min(48, _fontSize + delta));
            if (size == _fontSize) return;
            _fontSize = size;
            _rich.Font = new Font("Microsoft YaHei", _fontSize, FontStyle.Regular);
            if (!_manualSize) Height = CalcAutoHeight();
            LayoutAll();
            if (FontSizeChanged != null) FontSizeChanged(this, EventArgs.Empty);
        }

        public void CopyText()
        {
            if (_rich.TextLength == 0) return;
            if (_rich.SelectionLength > 0)
            {
                _rich.Copy();
            }
            else
            {
                _rich.SelectAll();
                _rich.Copy();
                _rich.Select(0, 0);
            }
            _footer.Text = "已复制到剪贴板";
        }

        public void SetAutoState(bool on)
        {
            _autoOn = on;
            _btnPause.Text = on ? "暂停 F9" : "继续 F9";
            _btnPause.ForeColor = on ? Color.FromArgb(120, 220, 140) : Color.FromArgb(255, 170, 80);
            _dot.Invalidate();
        }

        void DotPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_autoOn)
            {
                float k = (float)((Math.Sin(_phase) + 1.0) / 2.0); // 0..1
                int alpha = 80 + (int)(k * 175);
                float r = 3.1f + k * 2.6f;

                using (SolidBrush glow = new SolidBrush(Color.FromArgb(46, 90, 220, 130)))
                    g.FillEllipse(glow, 0, 0, 12, 12);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 90, 220, 130)))
                    g.FillEllipse(b, 6 - r, 6 - r, r * 2, r * 2);
            }
            else
            {
                // 暂停中：橙色静止小点
                using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 255, 170, 80)))
                    g.FillEllipse(b, 3.8f, 3.8f, 4.4f, 4.4f);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pulse.Dispose();
                _loadTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void ApplyConfig(AppConfig cfg)
        {
            _fontSize = Math.Max(10, Math.Min(48, cfg.FontSize));
            _hotkey = cfg.Hotkey;
            _origExpanded = cfg.OriginalExpanded;

            // 双模型时下拉显示识别模型，与设置保持一致
            string show = cfg.SplitModel ? cfg.OcrModel : cfg.Model;
            if (string.IsNullOrWhiteSpace(show)) show = cfg.Model;
            if (!string.IsNullOrWhiteSpace(show) && show != Convert.ToString(_models.Text))
                _models.Text = show;

            _rich.Font = new Font("Microsoft YaHei", _fontSize, FontStyle.Regular);
            Width = Math.Max(470, cfg.BoxWidth);
            if (cfg.BoxHeight > 120)
            {
                Height = cfg.BoxHeight;
                _manualSize = true;
            }
            if (_zh.Length > 0) ShowResult(_zh, _ja, _footer.Text);
            else ShowIdleHint();
        }

        public void ShowIdleHint()
        {
            _loading = false;
            _loadTimer.Stop();
            _rich.Visible = true;
            _zh = "";
            _ja = "";
            _title.Text = "译文";
            _rich.ForeColor = Color.FromArgb(140, 146, 160);
            _rich.Text = "按 " + _hotkey + " 框选日文区域";
            _origH = 0;
            _orig.Visible = false;
            _origToggle.Visible = false;
            _footer.Text = "拖动标题可移动 · 右下角可缩放";
            if (!_manualSize) Height = CalcAutoHeight();
            LayoutAll();
        }

        public void ShowLoading()
        {
            _title.Text = "识别中…";
            _ja = "";
            _origH = 0;
            _orig.Visible = false;
            _origToggle.Visible = false;
            _footer.Text = "";

            _loading = true;
            _progress = 0f;
            _rich.Visible = false;
            _loadTimer.Start();

            if (!_manualSize) Height = CalcAutoHeight();
            LayoutAll();
        }

        public void ShowError(string message)
        {
            _title.Text = "出错了";
            _loading = false;
            _loadTimer.Stop();
            _rich.Visible = true;
            _zh = "";
            _ja = "";
            _rich.ForeColor = Color.FromArgb(255, 140, 140);
            _rich.Text = message;
            _origH = 0;
            _orig.Visible = false;
            _footer.Text = "可在设置中检查 Key / 模型 / 网络";
            if (!_manualSize) Height = CalcAutoHeight();
            LayoutAll();
        }

        public void ShowResult(string zh, string ja, string meta)
        {
            _loading = false;
            _loadTimer.Stop();
            _rich.Visible = true;
            _zh = zh ?? "";
            _ja = ja ?? "";
            _title.Text = "译文";

            _rich.ForeColor = Color.White;
            _rich.Text = string.IsNullOrWhiteSpace(_zh) ? "（没有识别到日文）" : _zh;
            _rich.SelectionStart = 0;
            _rich.SelectionLength = 0;
            _rich.ScrollToCaret();

            bool hasJa = !string.IsNullOrWhiteSpace(_ja);
            _orig.Text = hasJa ? _ja : "";
            _orig.Visible = hasJa && _origExpanded;
            _origToggle.Visible = hasJa;
            _origToggle.Text = (_origExpanded ? "▼ 原文（点击收起）" : "▶ 原文（点击展开）");
            if (hasJa)
            {
                _orig.SelectionStart = 0;
                _orig.SelectionLength = 0;
                _orig.ScrollToCaret();
            }

            int w = Width - Pad * 2;
            _origH = hasJa ? Math.Min(110, Measure(_orig.Text, _orig.Font, w)) : 0;

            _footer.Text = meta;
            if (!_manualSize) Height = CalcAutoHeight();
            LayoutAll();
        }

        int Measure(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Size sz = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return sz.Height + 4;
        }

        int CalcAutoHeight()
        {
            int w = Width - Pad * 2;
            int richH = Math.Min(520, Math.Max(60, Measure(_rich.Text, _rich.Font, w)));
            bool hasJa = !string.IsNullOrWhiteSpace(_ja);
            int toggleH = hasJa ? 26 : 0;
            int origH = (hasJa && _origExpanded) ? _origH + 4 : 0;
            return Pad + HeaderH + 4 + richH + toggleH + origH + 6 + FooterH + Pad;
        }

        void ToggleOriginal()
        {
            if (string.IsNullOrWhiteSpace(_ja)) return;
            _origExpanded = !_origExpanded;

            _orig.Visible = _origExpanded;
            _origToggle.Text = (_origExpanded ? "▼ 原文（点击收起）" : "▶ 原文（点击展开）");

            int delta = (_origExpanded ? 1 : -1) * (_origH + 26);
            if (_manualSize) Height = Math.Max(180, Height + delta);
            else Height = CalcAutoHeight();

            LayoutAll();
            if (OriginalToggled != null) OriginalToggled(this, EventArgs.Empty);
        }

        void LayoutAll()
        {
            _title.SetBounds(Pad, Pad, 90, HeaderH);

            int bx = Width - Pad;
            bx -= 26; _btnClose.SetBounds(bx, Pad + 3, 24, 22);
            bx -= 50; _btnMore.SetBounds(bx, Pad + 3, 44, 22);
            bx -= 50; _btnHideAll.SetBounds(bx, Pad + 3, 44, 22);
            bx -= 78; _btnRetry.SetBounds(bx, Pad + 3, 72, 22);
            bx -= 50; _btnCopy.SetBounds(bx, Pad + 3, 44, 22);
            bx -= 82; _btnPause.SetBounds(bx, Pad + 3, 76, 22);
            bx -= 16; _dot.SetBounds(bx, Pad + 6, 12, 12);

            int y = Pad + HeaderH + 4;
            int contentW = Width - Pad * 2;
            int footerY = Height - Pad - FooterH;

            bool hasJa = !string.IsNullOrWhiteSpace(_ja);
            int toggleH = hasJa ? 26 : 0;
            int origH = (hasJa && _origExpanded) ? _origH : 0;

            int richBottom = footerY - toggleH - (origH > 0 ? origH + 4 : 0) - 6;
            int richH = Math.Max(40, richBottom - y);
            _rich.SetBounds(Pad, y, contentW, richH);

            int toggleY = y + richH + 2;
            _origToggle.Visible = hasJa;
            if (hasJa) _origToggle.SetBounds(Pad, toggleY, 200, 20);

            _orig.Visible = hasJa && origH > 0;
            if (origH > 0) _orig.SetBounds(Pad, toggleY + 24, contentW, origH);

            _models.SetBounds(Pad, footerY + 5, 140, 24);
            _btnReselect.SetBounds(Pad + 148, footerY + 7, 46, 20);
            _btnFontMinus.SetBounds(Pad + 202, footerY + 7, 28, 20);
            _btnFontPlus.SetBounds(Pad + 234, footerY + 7, 28, 20);

            int infoX = Pad + 270;
            _footer.SetBounds(infoX, footerY + 4, Math.Max(60, Width - infoX - Pad), 24);

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            using (Pen pen = new Pen(Color.FromArgb(70, 90, 120, 160), 1f))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            // 加载动画：进度条 + 骨架行（不显示文字）
            if (_loading)
            {
                int x = Pad;
                int y = _rich.Top + 8;
                int w = Width - Pad * 2;
                if (w > 40)
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(46, 52, 64)))
                        g.FillRectangle(bg, x, y, w, 4);
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(90, 200, 255)))
                        g.FillRectangle(fg, x, y, Math.Max(3, (int)(w * _progress)), 4);

                    using (SolidBrush sk = new SolidBrush(Color.FromArgb(40, 46, 57)))
                    {
                        g.FillRectangle(sk, x, y + 26, (int)(w * 0.88), 10);
                        g.FillRectangle(sk, x, y + 46, (int)(w * 0.72), 10);
                        g.FillRectangle(sk, x, y + 66, (int)(w * 0.55), 10);
                    }
                }
            }

            // 右下角缩放柄
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen grip = new Pen(Color.FromArgb(110, 120, 135), 1.4f))
            {
                int x0 = Width - 4;
                int y0 = Height - 4;
                for (int i = 0; i < 3; i++)
                    g.DrawLine(grip, x0 - 4 - i * 5, y0, x0, y0 - 4 - i * 5);
            }
        }

        public void PositionNear(Rectangle anchor, Rectangle virtualScreen)
        {
            int x = anchor.Left;
            int y = anchor.Bottom + 10;
            if (y + Height > virtualScreen.Bottom) y = anchor.Top - Height - 10;
            if (y < virtualScreen.Top) y = virtualScreen.Top + 10;
            if (x + Width > virtualScreen.Right) x = virtualScreen.Right - Width - 10;
            if (x < virtualScreen.Left) x = virtualScreen.Left + 10;
            Location = new Point(x, y);
        }
    }
}
