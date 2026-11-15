using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ScreenTrans
{
    public class SettingsForm : Form
    {
        readonly AppConfig _cfg;
        readonly ComboBox _preset = new ComboBox();
        readonly TextBox _base = new TextBox();
        readonly TextBox _key = new TextBox();
        readonly ComboBox _model = new ComboBox();
        readonly Button _fetch = new Button();
        readonly Label _price = new Label();
        readonly CheckBox _split = new CheckBox();
        readonly ComboBox _ocrModel = new ComboBox();
        readonly ComboBox _transModel = new ComboBox();
        readonly CheckBox _showKey = new CheckBox();
        readonly ComboBox _hotkey = new ComboBox();
        readonly CheckBox _auto = new CheckBox();
        readonly NumericUpDown _interval = new NumericUpDown();
        readonly NumericUpDown _fontSize = new NumericUpDown();
        readonly NumericUpDown _boxWidth = new NumericUpDown();
        readonly CheckBox _showOrig = new CheckBox();
        readonly Button _test = new Button();
        readonly Label _testResult = new Label();

        public AppConfig Result { get; private set; }

        public SettingsForm(AppConfig cfg)
        {
            _cfg = cfg.Clone();
            Text = "设置 · 屏幕日文翻译";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 676);
            TopMost = true;
            Font = new Font("Microsoft YaHei", 9f);
            BackColor = Color.FromArgb(245, 246, 250);

            BuildUi();
            LoadValues();
        }

        void BuildUi()
        {
            int x = 22, y = 16, w = 516;

            AddLabel("服务预设", x, y);
            _preset.SetBounds(x + 90, y - 2, 200, 26);
            _preset.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var p in Preset.All) _preset.Items.Add(p.Name);
            _preset.SelectedIndexChanged += PresetChanged;
            Controls.Add(_preset);
            y += 36;

            AddLabel("接口地址", x, y);
            _base.SetBounds(x + 90, y - 2, w - 90, 26);
            Controls.Add(_base);
            y += 34;

            AddLabel("API Key", x, y);
            _key.SetBounds(x + 90, y - 2, w - 160, 26);
            _key.UseSystemPasswordChar = true;
            Controls.Add(_key);
            _showKey.Text = "显示";
            _showKey.SetBounds(w - 52, y - 1, 60, 24);
            _showKey.CheckedChanged += (s, e) => _key.UseSystemPasswordChar = !_showKey.Checked;
            Controls.Add(_showKey);
            y += 34;

            AddLabel("模型名称", x, y);
            _model.SetBounds(x + 90, y - 2, w - 200, 26);
            _model.DropDownStyle = ComboBoxStyle.DropDown;
            _model.DrawMode = DrawMode.OwnerDrawFixed;
            _model.ItemHeight = 20;
            _model.DropDownWidth = 470;
            _model.DrawItem += ModelDrawItem;
            _model.SelectedIndexChanged += delegate { UpdatePriceLabel(); };
            _model.TextChanged += delegate { UpdatePriceLabel(); };
            Controls.Add(_model);

            _fetch.Text = "获取列表";
            _fetch.SetBounds(x + 90 + (w - 200) + 8, y - 2, 88, 26);
            _fetch.Click += async delegate { await FetchModelsAsync(); };
            Controls.Add(_fetch);
            y += 32;

            _price.SetBounds(x + 90, y, w - 90, 22);
            _price.ForeColor = Color.FromArgb(105, 110, 124);
            _price.Text = "";
            Controls.Add(_price);
            y += 26;

            _split.Text = "分两步：视觉模型只识别日文 → 文本模型翻译（更省 token）";
            _split.SetBounds(x, y, w, 24);
            _split.CheckedChanged += delegate { UpdateSplitState(); };
            Controls.Add(_split);
            y += 26;

            AddLabelAt("识别模型", x + 90, y);
            _ocrModel.SetBounds(x + 152, y - 2, 150, 26);
            _ocrModel.DropDownStyle = ComboBoxStyle.DropDown;
            _ocrModel.DrawMode = DrawMode.OwnerDrawFixed;
            _ocrModel.ItemHeight = 20;
            _ocrModel.DropDownWidth = 470;
            _ocrModel.DrawItem += ModelDrawItem;
            _ocrModel.TextChanged += delegate { UpdatePriceLabel(); };
            Controls.Add(_ocrModel);

            AddLabelAt("翻译模型", x + 312, y);
            _transModel.SetBounds(x + 374, y - 2, 164, 26);
            _transModel.DropDownStyle = ComboBoxStyle.DropDown;
            _transModel.DrawMode = DrawMode.OwnerDrawFixed;
            _transModel.ItemHeight = 20;
            _transModel.DropDownWidth = 470;
            _transModel.DrawItem += ModelDrawItem;
            _transModel.TextChanged += delegate { UpdatePriceLabel(); };
            Controls.Add(_transModel);
            y += 34;

            _test.Text = "测试连接";
            _test.SetBounds(x + 90, y, 96, 28);
            _test.Click += async (s, e) => await TestAsync();
            Controls.Add(_test);
            _testResult.SetBounds(x + 196, y + 4, w - 200, 22);
            _testResult.ForeColor = Color.Gray;
            Controls.Add(_testResult);
            y += 42;

            var g1 = new GroupBox();
            g1.Text = "操作";
            g1.SetBounds(x, y, w, 96);
            Controls.Add(g1);

            AddLabelIn(g1, "呼出热键", 14, 26);
            _hotkey.SetBounds(96, 22, 140, 26);
            _hotkey.DropDownStyle = ComboBoxStyle.DropDownList;
            _hotkey.Items.AddRange(new object[] { "F7", "F8", "F9", "F10", "F11", "F12", "Ctrl+Alt+Q", "Ctrl+Alt+S" });
            g1.Controls.Add(_hotkey);

            _auto.Text = "自动跟随（画面文字变化时重新翻译）";
            _auto.SetBounds(14, 58, 250, 24);
            g1.Controls.Add(_auto);

            _interval.SetBounds(270, 58, 60, 24);
            _interval.Minimum = 1; _interval.Maximum = 60;
            g1.Controls.Add(_interval);
            AddLabelIn(g1, "秒", 334, 62);
            y += 108;

            var g2 = new GroupBox();
            g2.Text = "显示";
            g2.SetBounds(x, y, w, 128);
            Controls.Add(g2);

            AddLabelIn(g2, "译文字号", 14, 26);
            _fontSize.SetBounds(96, 22, 60, 24);
            _fontSize.Minimum = 10; _fontSize.Maximum = 48;
            g2.Controls.Add(_fontSize);

            AddLabelIn(g2, "译文框宽度", 190, 26);
            _boxWidth.SetBounds(280, 22, 70, 24);
            _boxWidth.Minimum = 320; _boxWidth.Maximum = 1000;
            _boxWidth.Increment = 20;
            g2.Controls.Add(_boxWidth);
            AddLabelIn(g2, "px", 356, 26);

            _showOrig.Text = "同时显示日文原文";
            _showOrig.SetBounds(14, 64, 200, 24);
            g2.Controls.Add(_showOrig);
            y += 140;

            var ok = new Button();
            ok.Text = "保存";
            ok.SetBounds(w - 80, y, 80, 30);
            ok.DialogResult = DialogResult.OK;
            ok.Click += (s, e) => SaveValues();
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(w - 168, y, 80, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        void AddLabel(string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y + 3, 86, 22);
            l.ForeColor = Color.FromArgb(70, 74, 86);
            Controls.Add(l);
        }

        void AddLabelAt(string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y + 3, 58, 22);
            l.ForeColor = Color.FromArgb(70, 74, 86);
            Controls.Add(l);
        }

        static void AddLabelIn(Control parent, string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y + 3, 86, 22);
            l.ForeColor = Color.FromArgb(70, 74, 86);
            parent.Controls.Add(l);
        }

        void LoadValues()
        {
            _preset.SelectedItem = _cfg.Preset;
            if (_preset.SelectedIndex < 0) _preset.SelectedIndex = 0;

            _base.Text = _cfg.ApiBase;
            _key.Text = _cfg.ApiKey;
            _model.Text = _cfg.Model;

            _split.Checked = _cfg.SplitModel;
            _ocrModel.Text = string.IsNullOrWhiteSpace(_cfg.OcrModel) ? _cfg.Model : _cfg.OcrModel;
            _transModel.Text = _cfg.TransModel;
            UpdateSplitState();

            _hotkey.SelectedItem = _cfg.Hotkey;
            if (_hotkey.SelectedIndex < 0) _hotkey.SelectedIndex = 1;
            _auto.Checked = _cfg.AutoMode;
            _interval.Value = Math.Max(1, Math.Min(60, _cfg.AutoIntervalSec));
            _fontSize.Value = Math.Max(10, Math.Min(48, _cfg.FontSize));
            _boxWidth.Value = Math.Max(320, Math.Min(1000, _cfg.BoxWidth));
            _showOrig.Checked = _cfg.ShowOriginal;
        }

        void PresetChanged(object sender, EventArgs e)
        {
            var p = Preset.Find(Convert.ToString(_preset.SelectedItem));
            if (p.Base.Length > 0)
            {
                _base.Text = p.Base;
                _model.Text = p.Model;
            }
        }

        async Task TestAsync()
        {
            _test.Enabled = false;
            _testResult.ForeColor = Color.Gray;
            _testResult.Text = "测试中…";
            try
            {
                string url = _base.Text.TrimEnd('/') + "/models";
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(20);
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + _key.Text.Trim());
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        _testResult.ForeColor = Color.FromArgb(20, 140, 60);
                        _testResult.Text = "连接成功，Key 可用";
                    }
                    else
                    {
                        _testResult.ForeColor = Color.FromArgb(190, 60, 60);
                        _testResult.Text = "HTTP " + (int)resp.StatusCode + "（部分服务不支持 /models，可直接保存后试翻译）";
                    }
                }
            }
            catch (Exception ex)
            {
                _testResult.ForeColor = Color.FromArgb(190, 60, 60);
                _testResult.Text = "失败：" + ex.Message;
            }
            finally { _test.Enabled = true; }
        }

        void SaveValues()
        {
            _cfg.Preset = Convert.ToString(_preset.SelectedItem);
            _cfg.ApiBase = _base.Text.Trim();
            _cfg.ApiKey = _key.Text.Trim();
            _cfg.Model = _model.Text.Trim();
            _cfg.SplitModel = _split.Checked;
            _cfg.OcrModel = _ocrModel.Text.Trim();
            _cfg.TransModel = _transModel.Text.Trim();
            _cfg.Hotkey = Convert.ToString(_hotkey.SelectedItem);
            _cfg.AutoMode = _auto.Checked;
            _cfg.AutoIntervalSec = (int)_interval.Value;
            _cfg.FontSize = (int)_fontSize.Value;
            _cfg.BoxWidth = (int)_boxWidth.Value;
            _cfg.ShowOriginal = _showOrig.Checked;
            Result = _cfg;
        }

        // ---------------- 模型列表与价格 ----------------
        class ModelItem
        {
            public string Id;
            public double? InUsdPerM;
            public double? OutUsdPerM;

            public ModelItem(string id, double? inUsdPerM, double? outUsdPerM)
            {
                Id = id;
                InUsdPerM = inUsdPerM;
                OutUsdPerM = outUsdPerM;
            }

            public override string ToString() { return Id; }
        }

        void ModelDrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || e.Index < 0 || e.Index >= combo.Items.Count) return;
            e.DrawBackground();

            ModelItem item = combo.Items[e.Index] as ModelItem;
            string id = item != null ? item.Id : Convert.ToString(combo.Items[e.Index]);
            string price = item != null
                ? Pricing.ShortText(item.Id, item.InUsdPerM, item.OutUsdPerM)
                : Pricing.ShortText(id, null, null);

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color fg = selected ? SystemColors.HighlightText : Color.FromArgb(40, 44, 52);
            Color priceFg = selected ? SystemColors.HighlightText : Color.FromArgb(135, 140, 152);

            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(fg))
                e.Graphics.DrawString(id, e.Font, b, r.Left + 4, r.Top + 3);

            if (price.Length > 0)
            {
                using (Font pf = new Font("Microsoft YaHei", 8.5f))
                {
                    SizeF sz = e.Graphics.MeasureString(price, pf);
                    using (SolidBrush b2 = new SolidBrush(priceFg))
                        e.Graphics.DrawString(price, pf, b2, r.Right - sz.Width - 6, r.Top + 4);
                }
            }
        }

        void UpdatePriceLabel()
        {
            if (_split.Checked)
            {
                string ocr = _ocrModel.Text.Trim();
                string tr = _transModel.Text.Trim();

                string warn = UnsupportedReason(ocr);
                if (warn.Length == 0) warn = UnsupportedReason(tr);
                if (warn.Length > 0)
                {
                    _price.ForeColor = Color.FromArgb(200, 60, 60);
                    _price.Text = "⚠ " + warn;
                    return;
                }

                _price.ForeColor = Color.FromArgb(105, 110, 124);
                string a = ocr.Length == 0 ? "未选" : Pricing.DetailText(ocr);
                string b = tr.Length == 0 ? "未选" : Pricing.DetailText(tr);
                _price.Text = "识别：" + a + "   翻译：" + b;
                return;
            }

            string m = _model.Text.Trim();
            if (m.Length == 0) { _price.Text = "请从列表选择，或直接输入模型 ID"; return; }

            string unsupported = UnsupportedReason(m);
            if (unsupported.Length > 0)
            {
                _price.ForeColor = Color.FromArgb(200, 60, 60);
                _price.Text = "⚠ " + unsupported;
                return;
            }
            _price.ForeColor = Color.FromArgb(105, 110, 124);

            ModelItem item = _model.SelectedItem as ModelItem;
            if (item != null && item.InUsdPerM.HasValue)
            {
                _price.Text = "价格：" + Pricing.ShortText(item.Id, item.InUsdPerM, item.OutUsdPerM)
                    + " /百万tokens（接口返回，USD 折算）";
            }
            else
            {
                _price.Text = "参考价：" + Pricing.DetailText(m) + "（以官网为准）";
            }
        }

        void UpdateSplitState()
        {
            bool on = _split.Checked;
            _ocrModel.Enabled = on;
            _transModel.Enabled = on;
            _model.Enabled = !on;
            _model.BackColor = on ? Color.FromArgb(238, 238, 242) : SystemColors.Window;
            UpdatePriceLabel();
        }

        async Task FetchModelsAsync()
        {
            string baseUrl = _base.Text.Trim();
            string key = _key.Text.Trim();
            if (baseUrl.Length == 0) { MessageBox.Show("请先填写接口地址。", "获取模型列表"); return; }
            if (key.Length == 0) { MessageBox.Show("请先填写 API Key。", "获取模型列表"); return; }

            _fetch.Enabled = false;
            _fetch.Text = "获取中…";
            string oldModel = _model.Text.Trim();

            try
            {
                string url = baseUrl.TrimEnd('/') + "/models";
                using (HttpClient http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + key);

                    HttpResponseMessage resp = await http.GetAsync(url);
                    string json = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        MessageBox.Show("获取失败：HTTP " + (int)resp.StatusCode + "\r\n" + Shorten(json),
                            "获取模型列表");
                        return;
                    }

                    List<ModelItem> all = ParseModels(json, false);
                    List<ModelItem> vision = ParseModels(json, true);
                    if (vision.Count == 0 && all.Count == 0)
                    {
                        MessageBox.Show("接口没有返回可用模型。", "获取模型列表");
                        return;
                    }

                    string oldOcr = _ocrModel.Text.Trim();
                    string oldTrans = _transModel.Text.Trim();

                    _model.Items.Clear();
                    _ocrModel.Items.Clear();
                    foreach (ModelItem it in vision) { _model.Items.Add(it); _ocrModel.Items.Add(it); }

                    _transModel.Items.Clear();
                    foreach (ModelItem it in all) _transModel.Items.Add(it);

                    SelectOrSet(_model, oldModel);
                    SelectOrSet(_ocrModel, oldOcr);
                    SelectOrSet(_transModel, oldTrans);

                    UpdatePriceLabel();
                    _price.Text = "已获取 " + vision.Count + " 个视觉模型 / " + all.Count + " 个模型 · " + _price.Text;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("获取失败：" + ex.Message, "获取模型列表");
            }
            finally
            {
                _fetch.Enabled = true;
                _fetch.Text = "获取列表";
            }
        }

        static void SelectOrSet(ComboBox combo, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ModelItem it = combo.Items[i] as ModelItem;
                if (it != null && string.Equals(it.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.Text = id;
        }

        static List<ModelItem> ParseModels(string json, bool onlyVision)
        {
            List<ModelItem> list = new List<ModelItem>();
            JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
            if (root == null) return list;

            object dataObj;
            if (!root.TryGetValue("data", out dataObj)) return list;

            IEnumerable seq = dataObj as IEnumerable;
            if (seq == null) return list;

            foreach (object item in seq)
            {
                Dictionary<string, object> d = item as Dictionary<string, object>;
                if (d == null) continue;

                string id = d.ContainsKey("id") ? Convert.ToString(d["id"]) : "";
                if (id.Length == 0) continue;
                if (onlyVision)
                {
                    if (!LooksLikeVisionModel(id)) continue;
                }
                else
                {
                    // 翻译用：只保留能走文本 chat 接口的模型
                    if (!LooksLikeTextModel(id)) continue;
                }

                double? inUsd = null;
                double? outUsd = null;
                object pricingObj;
                if (d.TryGetValue("pricing", out pricingObj))
                {
                    Dictionary<string, object> pr = pricingObj as Dictionary<string, object>;
                    if (pr != null)
                    {
                        if (pr.ContainsKey("prompt"))
                        {
                            double v = ToDouble(pr["prompt"]);
                            if (v > 0) inUsd = v * 1000000.0; // USD/token → USD/1M
                        }
                        if (pr.ContainsKey("completion"))
                        {
                            double v = ToDouble(pr["completion"]);
                            if (v > 0) outUsd = v * 1000000.0;
                        }
                    }
                }
                list.Add(new ModelItem(id, inUsd, outUsd));
            }

            list.Sort(delegate(ModelItem a, ModelItem b)
            {
                return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static bool LooksLikeVisionModel(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("image") || s.Contains("speech") || s.Contains("tts")
                || s.Contains("asr") || s.Contains("audio") || s.Contains("voice"))
                return false;
            return s.Contains("vl") || s.Contains("omni") || s.Contains("vision")
                || s.Contains("4v") || s.Contains("ocr")
                || s.Contains("gpt-4o") || s.Contains("gpt-4.1") || s.Contains("gpt-5")
                || s.Contains("claude") || s.Contains("gemini") || s.Contains("kimi")
                || s.Contains("glm-4") || s.Contains("glm-5");
        }

        /// <summary>能否作为纯文本翻译模型（排除语音、实时、图像生成等专用模型）</summary>
        static bool LooksLikeTextModel(string id)
        {
            string s = id.ToLowerInvariant();
            string[] blocked = new string[]
            {
                "livetranslate", "realtime", "audio", "tts", "asr", "speech",
                "voice", "image", "wan", "embedding", "rerank", "omni-flash-realtime"
            };
            for (int i = 0; i < blocked.Length; i++)
                if (s.Contains(blocked[i])) return false;
            return true;
        }

        /// <summary>给用户看的不可用原因（不在这些模型上做文本翻译）</summary>
        static string UnsupportedReason(string id)
        {
            string s = (id ?? "").ToLowerInvariant();
            if (s.Contains("livetranslate") || s.Contains("realtime"))
                return "该模型是实时语音翻译模型，只支持音频流接口，不能用于文本翻译";
            if (s.Contains("tts") || s.Contains("speech") || s.Contains("voice"))
                return "该模型是语音合成模型，不能用于文本翻译";
            if (s.Contains("asr") || s.Contains("audio"))
                return "该模型是语音识别模型，不能用于文本翻译";
            if (s.Contains("image") || s.Contains("wan"))
                return "该模型是图像生成模型，不能用于文本翻译";
            return "";
        }

        static double ToDouble(object o)
        {
            if (o == null) return 0;
            if (o is double) return (double)o;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            double d;
            if (double.TryParse(Convert.ToString(o), out d)) return d;
            return 0;
        }

        static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
        }
    }
}
