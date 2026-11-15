using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ScreenTrans
{
    /// <summary>应用配置，持久化到 %APPDATA%\ScreenTransJa\config.json</summary>
    public class AppConfig
    {
        public string Preset = "通义千问";
        public string ApiBase = "https://dashscope.aliyuncs.com/compatible-mode/v1";
        public string ApiKey = "";
        public string Model = "qwen3-vl-plus";

        // 双模型模式：视觉模型只识别日文，文本模型负责翻译
        public bool SplitModel = false;
        public string OcrModel = "";    // 识别用（空 = 用 Model）
        public string TransModel = "";  // 翻译用（空 = 单模型模式）

        public string Hotkey = "F8";
        public bool AutoMode = true;
        public int AutoIntervalSec = 3;
        // 变化灵敏度：0 低（不易触发）1 中 2 高（更敏感）
        public int ChangeSensitivity = 1;

        public int FontSize = 20;
        public int BoxOpacity = 92;
        public int BoxWidth = 560;
        public int BoxHeight = 0; // 0 = 按内容自动高度
        public bool ShowOriginal = true;
        public bool OriginalExpanded = false; // 原文默认折叠

        // 上一次框选的屏幕坐标（自动模式复用）
        public int LastX = -1;
        public int LastY = -1;
        public int LastW = 0;
        public int LastH = 0;

        public string Prompt =
            "你是日文画面翻译助手。请按图片中文字的实际换行逐行读取日文，保留每行行首的符号" +
            "（如「」『』、・、-、※、数字编号等），不要合并成一段。\r\n" +
            "然后逐行翻译成自然、口语化的简体中文：译文行数必须与原文一致，逐行一一对应，行首符号原样保留。\r\n" +
            "只输出一个 JSON 对象，格式为 {\"ja\":\"日文原文（换行用 \\n 表示）\",\"zh\":\"中文译文（换行用 \\n 表示）\"}，" +
            "不要输出解释、不要使用 Markdown 代码块。若图中没有日文，则 ja 与 zh 都返回空字符串。";

        [ScriptIgnore]
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenTransJa"); }
        }

        [ScriptIgnore]
        public static string FilePath
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
                    var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = new JavaScriptSerializer().Serialize(this);
                File.WriteAllText(FilePath, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        public AppConfig Clone()
        {
            return (AppConfig)this.MemberwiseClone();
        }

        /// <summary>把设置窗口的结果复制进来（不动上次选区坐标）</summary>
        public void CopyFrom(AppConfig o)
        {
            if (o == null) return;
            Preset = o.Preset;
            ApiBase = o.ApiBase;
            ApiKey = o.ApiKey;
            Model = o.Model;
            SplitModel = o.SplitModel;
            OcrModel = o.OcrModel;
            TransModel = o.TransModel;
            Hotkey = o.Hotkey;
            AutoMode = o.AutoMode;
            AutoIntervalSec = o.AutoIntervalSec;
            FontSize = o.FontSize;
            BoxOpacity = o.BoxOpacity;
            BoxWidth = o.BoxWidth;
            ShowOriginal = o.ShowOriginal;
            Prompt = o.Prompt;
        }
    }

    /// <summary>常见多模态服务预设（OpenAI 兼容）</summary>
    public class Preset
    {
        public string Name;
        public string Base;
        public string Model;
        public Preset(string n, string b, string m) { Name = n; Base = b; Model = m; }

        public static List<Preset> All = new List<Preset>
        {
            new Preset("通义千问 Qwen-VL", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3-vl-plus"),
            new Preset("智谱 GLM-4V",       "https://open.bigmodel.cn/api/paas/v4",             "glm-4v-flash"),
            new Preset("OpenAI GPT-4o",     "https://api.openai.com/v1",                        "gpt-4o-mini"),
            new Preset("自定义",            "", "")
        };

        public static Preset Find(string name)
        {
            return All.Find(p => p.Name == name) ?? All[All.Count - 1];
        }
    }
}
