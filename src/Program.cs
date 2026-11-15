using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenTrans
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created = false;
            Mutex mutex = new Mutex(true, "ScreenTransJa_SingleInstance_v1", out created);
            if (!created)
            {
                MessageBox.Show("「屏幕日文翻译」已经在运行了，请看右下角托盘图标。", "屏幕日文翻译",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += new ThreadExceptionEventHandler(OnThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnDomainException);

            Controller ctx = new Controller();

            Rectangle rect = ParseRect(args);
            if (rect.Width >= 8 && rect.Height >= 8) ctx.QueueTranslate(rect);
            if (HasFlag(args, "--settings")) ctx.QueueSettings();

            Application.Run(ctx);

            GC.KeepAlive(mutex);
        }

        /// <summary>支持 --rect x,y,w,h 直接翻译固定区域（便于做快捷方式/自动化）</summary>
        static Rectangle ParseRect(string[] args)        {
            if (args == null) return Rectangle.Empty;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--rect", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string[] p = args[i + 1].Split(',');
                    int x, y, w, h;
                    if (p.Length == 4
                        && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)
                        && int.TryParse(p[2], out w) && int.TryParse(p[3], out h))
                        return new Rectangle(x, y, w, h);
                }
            }
            return Rectangle.Empty;
        }

        static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Fatal(e.Exception);
        }

        static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Fatal(e.ExceptionObject as Exception);
        }

        static void Fatal(Exception ex)
        {
            if (ex == null) return;
            try
            {
                MessageBox.Show("程序遇到错误：\r\n\r\n" + ex.Message, "屏幕日文翻译",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    /// <summary>全局调度：热键、选区、截图、调用模型、托盘</summary>
    public class Controller : ApplicationContext
    {
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int HOTKEY_TRANSLATE = 0x5A11;
        const int HOTKEY_AUTO = 0x5A12;
        const int HOTKEY_RETRY = 0x5A13;

        readonly AppConfig _cfg;
        readonly ResultForm _result;
        readonly HostForm _host;
        readonly NotifyIcon _tray;
        readonly ToolStripMenuItem _autoItem;
        readonly System.Windows.Forms.Timer _autoTimer;

        bool _busy;
        bool _autoOn;
        bool _overlayOpen;
        bool _resultShown;
        Rectangle _lastRect = Rectangle.Empty;
        readonly ChangeDetector _detector = new ChangeDetector();
        readonly SelectionFrame _frame = new SelectionFrame();
        ToolStripMenuItem _frameItem;
        System.Windows.Forms.Timer _pendingTimer;
        System.Windows.Forms.Timer _settingsTimer;
        Rectangle _pendingRect = Rectangle.Empty;

        public Controller()
        {
            _cfg = AppConfig.Load();
            _detector.Threshold = SensitivityThreshold(_cfg.ChangeSensitivity);

            _result = new ResultForm(_cfg);
            _result.RetryRequested += new EventHandler(OnRetryRequested);
            _result.SettingsRequested += new EventHandler(OnSettingsRequested);
            _result.PauseRequested += new EventHandler(OnPauseRequested);
            _result.ModelChanged += new EventHandler(OnModelChanged);
            _result.FontSizeChanged += new EventHandler(OnFontSizeChanged);
            _result.SizeChanged += new EventHandler(OnResultSizeChanged);
            _result.ReselectRequested += new EventHandler(OnReselectRequested);
            _result.OriginalToggled += new EventHandler(OnOriginalToggled);
            _result.HideAllRequested += new EventHandler(OnHideAllRequested);

            _host = new HostForm(this);
            IntPtr handle = _host.Handle; // 强制创建句柄，用于热键
            GC.KeepAlive(handle);

            _autoTimer = new System.Windows.Forms.Timer();
            _autoTimer.Tick += new EventHandler(OnAutoTimer);

            _autoItem = new ToolStripMenuItem("自动跟随（F9）");
            _autoItem.Click += new EventHandler(OnToggleAuto);

            _tray = CreateTray();
            _tray.Visible = true;

            if (_cfg.LastW > 0 && _cfg.LastH > 0 && _cfg.LastX > -10000)
                _lastRect = new Rectangle(_cfg.LastX, _cfg.LastY, _cfg.LastW, _cfg.LastH);

            if (_lastRect.Width > 0)
            {
                _frame.SetArea(_lastRect);
                _frame.Show();
            }

            RegisterHotkeys();

            if (_cfg.AutoMode) StartAuto(false);
            else _result.SetAutoState(false);

            if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
            {
                _tray.ShowBalloonTip(4000, "还需要一步", "请先在设置里填写大模型 API Key。", ToolTipIcon.Info);
                OpenSettings();
            }
        }

        // ---------------- 热键 ----------------
        static bool ParseHotkey(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().ToUpperInvariant();
                if (p == "CTRL" || p == "CONTROL") mods |= 0x0002;
                else if (p == "ALT") mods |= 0x0001;
                else if (p == "SHIFT") mods |= 0x0004;
                else if (p.Length >= 2 && p[0] == 'F')
                {
                    int fn;
                    if (int.TryParse(p.Substring(1), out fn) && fn >= 1 && fn <= 24)
                        vk = (uint)(0x70 + fn - 1);
                }
                else if (p.Length == 1)
                {
                    vk = (uint)char.ToUpperInvariant(p[0]);
                }
            }
            return vk != 0;
        }

        void RegisterHotkeys()
        {
            UnregisterHotKey(_host.Handle, HOTKEY_TRANSLATE);
            UnregisterHotKey(_host.Handle, HOTKEY_AUTO);
            UnregisterHotKey(_host.Handle, HOTKEY_RETRY);

            uint mods;
            uint vk;
            if (!ParseHotkey(_cfg.Hotkey, out mods, out vk))
            {
                MessageBox.Show("无法识别热键设置：" + _cfg.Hotkey, "屏幕日文翻译");
            }
            else if (!RegisterHotKey(_host.Handle, HOTKEY_TRANSLATE, mods | 0x4000, vk)) // MOD_NOREPEAT
            {
                MessageBox.Show("热键 " + _cfg.Hotkey + " 注册失败，可能已被其他程序占用。\r\n可在设置里换一个。",
                    "屏幕日文翻译");
            }

            // F9：暂停/继续自动跟随；F10：重译上次区域
            RegisterHotKey(_host.Handle, HOTKEY_AUTO, 0x4000, 0x78);
            RegisterHotKey(_host.Handle, HOTKEY_RETRY, 0x4000, 0x79);
        }

        public void OnHotkey(int id)
        {
            if (id == HOTKEY_TRANSLATE)
            {
                if (_overlayOpen) return;
                PickArea();
            }
            else if (id == HOTKEY_AUTO)
            {
                ToggleAuto();
            }
            else if (id == HOTKEY_RETRY)
            {
                if (_lastRect.Width >= 8) Translate(_lastRect);
            }
        }

        /// <summary>延迟打开设置窗口（命令行 --settings 用）</summary>
        public void QueueSettings()
        {
            // 通过窗口消息队列触发，确保在消息循环启动后执行
            _host.BeginInvoke(new MethodInvoker(OpenSettings));
        }

        /// <summary>延迟 0.5 秒后翻译指定区域（命令行 --rect 用）</summary>
        public void QueueTranslate(Rectangle r)
        {
            _lastRect = r; // 让后续自动跟随基于这个区域
            _frame.SetArea(r);
            _frame.Show();
            _pendingRect = r;
            _pendingTimer = new System.Windows.Forms.Timer();
            _pendingTimer.Interval = 500;
            _pendingTimer.Tick += new EventHandler(OnPendingTick);
            _pendingTimer.Start();
        }

        void OnPendingTick(object sender, EventArgs e)
        {
            if (_pendingTimer != null)
            {
                _pendingTimer.Stop();
                _pendingTimer.Dispose();
                _pendingTimer = null;
            }
            if (_pendingRect.Width >= 8) Translate(_pendingRect);
        }

        // ---------------- 选区与翻译 ----------------
        void PickArea()
        {
            if (_overlayOpen) return;
            _overlayOpen = true;

            // 先隐藏译文框，避免它被截进冻结画面里
            bool restoreResult = _result.Visible;
            if (restoreResult)
            {
                _result.Hide();
                Application.DoEvents();
                System.Threading.Thread.Sleep(150);
                Application.DoEvents();
            }

            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                Bitmap screen = Capture(vs);
                OverlayForm ov = new OverlayForm(vs, screen);
                try
                {
                    DialogResult dr = ov.ShowDialog();
                    if (dr == DialogResult.OK && ov.Selection.Width >= 8)
                    {
                        _lastRect = ov.Selection;
                        _cfg.LastX = _lastRect.X;
                        _cfg.LastY = _lastRect.Y;
                        _cfg.LastW = _lastRect.Width;
                        _cfg.LastH = _lastRect.Height;
                        _cfg.Save();
                        _frame.SetArea(_lastRect);
                        _frame.Show();
                        _detector.Reset(); // 新区域重新建立基准
                        if (restoreResult) _result.Show();
                        restoreResult = false;
                        Translate(_lastRect);
                        // 框选完成后自动进入跟随模式
                        if (_cfg.AutoMode) StartAuto(false);
                    }
                }
                finally
                {
                    ov.Dispose();
                }
            }
            finally
            {
                if (restoreResult && _resultShown) _result.Show();
                _overlayOpen = false;
            }
        }

        static Bitmap Capture(Rectangle r)
        {
            Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
            Graphics g = Graphics.FromImage(bmp);
            try
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
            }
            finally
            {
                g.Dispose();
            }
            return bmp;
        }

        async void Translate(Rectangle rect)
        {
            if (_busy) return;

            Rectangle vs = SystemInformation.VirtualScreen;
            // 四周留 3px 余量，避免把边缘的字切掉一半（OCR 会认错）
            rect = Rectangle.Inflate(rect, 3, 3);
            rect.Intersect(vs);
            if (rect.Width < 8 || rect.Height < 8) return;

            _busy = true;
            try
            {
                using (Bitmap bmp = Capture(rect))
                {
                    _result.ShowLoading();
                    if (!_resultShown)
                    {
                        _result.PositionNear(rect, vs);
                        _result.Show();
                        _resultShown = true;
                    }
                    else
                    {
                        _result.Show();
                    }

                    Stopwatch sw = Stopwatch.StartNew();
                    TranslationResult r = await Translator.TranslateAsync(bmp, _cfg);
                    sw.Stop();

                    if (r.Ok)
                    {
                        string modelText;
                        if (_cfg.SplitModel && !string.IsNullOrWhiteSpace(_cfg.TransModel))
                        {
                            string ocrModel = string.IsNullOrWhiteSpace(_cfg.OcrModel) ? _cfg.Model : _cfg.OcrModel;
                            modelText = "双模型 · " + ocrModel + " → " + _cfg.TransModel;
                        }
                        else
                        {
                            modelText = "单模型 · " + _cfg.Model;
                        }
                        string meta = modelText + " · " + sw.ElapsedMilliseconds + " ms · " + DateTime.Now.ToString("HH:mm:ss");
                        if (_autoOn) meta += " · 自动跟随 " + _cfg.AutoIntervalSec + "s";
                        _result.ShowResult(r.Zh, _cfg.ShowOriginal ? r.Ja : "", meta);

                        // 以实际翻译的这一帧为基准，避免同一画面被重复触发
                        try { _detector.MarkCurrent(bmp); } catch { }
                    }
                    else
                    {
                        _result.ShowError(r.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _result.ShowError("内部错误：" + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        void OnRetryRequested(object sender, EventArgs e)
        {
            if (_lastRect.Width > 0) Translate(_lastRect);
        }

        void OnSettingsRequested(object sender, EventArgs e)
        {
            OpenSettings();
        }

        void OnPauseRequested(object sender, EventArgs e)
        {
            ToggleAuto();
        }

        void OnModelChanged(object sender, EventArgs e)
        {
            string m = _result.SelectedModel;
            if (m.Length == 0 || m == _cfg.Model) return;
            _cfg.Model = m;
            _cfg.Save();
            if (_lastRect.Width > 0) Translate(_lastRect); // 立刻用新模型重译
        }

        void OnFontSizeChanged(object sender, EventArgs e)
        {
            _cfg.FontSize = _result.FontSize;
            _cfg.Save();
        }

        void OnResultSizeChanged(object sender, EventArgs e)
        {
            _cfg.BoxWidth = _result.Width;
            _cfg.BoxHeight = _result.Height;
            _cfg.Save();
        }

        void OnReselectRequested(object sender, EventArgs e)
        {
            PickArea();
        }

        void OnHideAllRequested(object sender, EventArgs e)
        {
            _result.Hide();
            _frame.Hide();
        }

        void OnOriginalToggled(object sender, EventArgs e)
        {
            _cfg.OriginalExpanded = _result.OriginalExpanded;
            _cfg.Save();
        }

        // ---------------- 自动模式 ----------------
        static double SensitivityThreshold(int level)
        {
            if (level <= 0) return 0.020; // 低：不太敏感
            if (level >= 2) return 0.006; // 高：更容易触发
            return 0.012;                 // 中
        }

        void OnAutoTimer(object sender, EventArgs e)
        {
            AutoTick();
        }

        void AutoTick()
        {
            if (!_autoOn || _busy) return;
            if (_lastRect.Width < 24 || _lastRect.Height < 16) return;

            try
            {
                // 必须与 Translate 里截图区域一致（同样外扩 3px），
                // 否则两帧尺寸不同，缩放后内容错位会产生假变化
                Rectangle vs = SystemInformation.VirtualScreen;
                Rectangle detRect = Rectangle.Inflate(_lastRect, 3, 3);
                detRect.Intersect(vs);

                using (Bitmap bmp = Capture(detRect))
                {
                    // 只认“文字区域”的变化，背景渐变/噪点不计
                    if (!_detector.HasTextChanged(bmp)) return;
                }
                Translate(_lastRect);
            }
            catch { }
        }

        
        void StartAuto(bool notify)
        {
            _autoOn = true;
            if (_autoItem != null) _autoItem.Checked = true;
            if (_result != null) _result.SetAutoState(true);
            _autoTimer.Interval = Math.Max(1, _cfg.AutoIntervalSec) * 1000;
            _autoTimer.Start();
            if (notify)
            {
                _tray.ShowBalloonTip(2000, "自动跟随已开启",
                    "每隔 " + _cfg.AutoIntervalSec + " 秒检查一次上次框选区域，文字变了就重新翻译。", ToolTipIcon.Info);
            }
        }

        void StopAuto()
        {
            _autoOn = false;
            if (_autoItem != null) _autoItem.Checked = false;
            if (_result != null) _result.SetAutoState(false);
            _autoTimer.Stop();
        }

        void OnToggleAuto(object sender, EventArgs e)
        {
            ToggleAuto();
        }

        void ToggleAuto()
        {
            if (_autoOn)
            {
                StopAuto();
                _cfg.AutoMode = false;
            }
            else
            {
                if (_lastRect.Width < 24)
                {
                    MessageBox.Show("请先按 " + _cfg.Hotkey + " 框选一次区域，再开启自动翻译。", "屏幕日文翻译");
                    return;
                }
                _cfg.AutoMode = true;
                StartAuto(true);
            }
            _cfg.Save();
        }

        // ---------------- 设置 ----------------
        void OpenSettings()
        {
            // 配置期间暂停后台翻译并收起译文框，避免盖住设置窗口
            bool autoWasOn = _autoOn;
            if (autoWasOn) StopAuto();
            bool wasVisible = _result.Visible;
            if (wasVisible) _result.Hide();

            SettingsForm f = new SettingsForm(_cfg);
            try
            {
                if (f.ShowDialog() != DialogResult.OK) return;

                string oldHotkey = _cfg.Hotkey;
                _cfg.CopyFrom(f.Result);
                _cfg.Save();

                _result.ApplyConfig(_cfg);
                _detector.Threshold = SensitivityThreshold(_cfg.ChangeSensitivity);
                RegisterHotkeys();
            }
            finally
            {
                f.Dispose();
                if (wasVisible) _result.Show();
                if (_cfg.AutoMode) StartAuto(false);
            }
        }

        // ---------------- 托盘 ----------------
        NotifyIcon CreateTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem miTranslate = new ToolStripMenuItem("开始翻译（" + _cfg.Hotkey + "）");
            miTranslate.Click += new EventHandler(OnMenuTranslate);
            menu.Items.Add(miTranslate);
            menu.Items.Add(_autoItem);
            ToolStripMenuItem miRetry = new ToolStripMenuItem("重译上次区域（F10）");
            miRetry.Click += new EventHandler(OnMenuRetry);
            menu.Items.Add(miRetry);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miShow = new ToolStripMenuItem("显示译文框");
            miShow.Click += new EventHandler(OnMenuShow);
            menu.Items.Add(miShow);

            _frameItem = new ToolStripMenuItem("显示识别框");
            _frameItem.Checked = true;
            _frameItem.Click += delegate { _frame.Visible = _frameItem.Checked; };
            menu.Items.Add(_frameItem);

            ToolStripMenuItem miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += new EventHandler(OnMenuSettings);
            menu.Items.Add(miSettings);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += new EventHandler(OnMenuExit);
            menu.Items.Add(miExit);

            NotifyIcon tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "屏幕日文翻译";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += new EventHandler(OnTrayDoubleClick);
            return tray;
        }

        void OnMenuTranslate(object sender, EventArgs e) { PickArea(); }
        void OnMenuRetry(object sender, EventArgs e) { if (_lastRect.Width >= 8) Translate(_lastRect); }
        void OnMenuShow(object sender, EventArgs e) { _result.Show(); _result.BringToFront(); _frame.Show(); }
        void OnMenuSettings(object sender, EventArgs e) { OpenSettings(); }
        void OnMenuExit(object sender, EventArgs e) { ExitApp(); }
        void OnTrayDoubleClick(object sender, EventArgs e) { PickArea(); }

        static Icon MakeIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                        Color.FromArgb(90, 200, 255), Color.FromArgb(125, 110, 240), 45f))
                    {
                        g.FillEllipse(b, 1, 1, 30, 30);
                    }
                    using (Font f = new Font("Microsoft YaHei", 15f, FontStyle.Bold))
                    using (SolidBrush w = new SolidBrush(Color.White))
                    {
                        SizeF s = g.MeasureString("译", f);
                        g.DrawString("译", f, w, (32 - s.Width) / 2f, (32 - s.Height) / 2f + 1);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
            finally
            {
                bmp.Dispose();
            }
        }

        void ExitApp()
        {
            try
            {
                _cfg.Save();
                UnregisterHotKey(_host.Handle, HOTKEY_TRANSLATE);
                UnregisterHotKey(_host.Handle, HOTKEY_AUTO);
                UnregisterHotKey(_host.Handle, HOTKEY_RETRY);
                _autoTimer.Stop();
                _tray.Visible = false;
                _tray.Dispose();
                _result.Dispose();
                _frame.Close();
                _host.Dispose();
            }
            catch { }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _tray.Dispose(); } catch { }
                try { _result.Dispose(); } catch { }
                try { _host.Dispose(); } catch { }
                try { _autoTimer.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>隐藏窗口，只用来接收 WM_HOTKEY</summary>
    public class HostForm : Form
    {
        readonly Controller _c;

        public HostForm(Controller c)
        {
            _c = c;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
            Width = 1;
            Height = 1;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false); // 永不真正显示
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) // WM_HOTKEY
            {
                _c.OnHotkey(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }
}
