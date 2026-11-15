using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenTrans
{
    public class TranslationResult
    {
        public string Ja = "";
        public string Zh = "";
        public string Error = "";
        public bool Ok { get { return string.IsNullOrEmpty(Error); } }
    }

    /// <summary>
    /// 两种调用方式：
    /// 1) 单模型（默认）：一次请求，视觉模型同时输出日文原文与中文译文；
    /// 2) 双模型：视觉模型只识别日文，再交给纯文本模型翻译（翻译价格更低）。
    /// </summary>
    public class Translator
    {
        static readonly HttpClient Http;

        static Translator()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 16;

            HttpClientHandler handler = new HttpClientHandler();
            handler.AllowAutoRedirect = true;
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            Http = new HttpClient(handler);
            Http.Timeout = TimeSpan.FromSeconds(90);
        }

        public static string EncodeJpeg(Bitmap bmp, long quality)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ImageCodecInfo codec = null;
                foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                    if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }

                if (codec == null) { bmp.Save(ms, ImageFormat.Jpeg); }
                else
                {
                    using (EncoderParameters ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                        bmp.Save(ms, codec, ep);
                    }
                }
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public static async Task<TranslationResult> TranslateAsync(Bitmap bmp, AppConfig cfg)
        {
            if (cfg.SplitModel && !string.IsNullOrWhiteSpace(cfg.TransModel))
                return await TwoStepAsync(bmp, cfg).ConfigureAwait(false);
            return await OneShotAsync(bmp, cfg).ConfigureAwait(false);
        }

        // ---------------- 单模型 ----------------
        static async Task<TranslationResult> OneShotAsync(Bitmap bmp, AppConfig cfg)
        {
            TranslationResult r = new TranslationResult();
            if (!CheckConfig(cfg, cfg.Model, r)) return r;

            string b64 = EncodeJpeg(bmp, 90);

            var textPart = new Dictionary<string, object>();
            textPart["type"] = "text";
            textPart["text"] = cfg.Prompt;

            var imageUrl = new Dictionary<string, object>();
            imageUrl["url"] = "data:image/jpeg;base64," + b64;

            var imagePart = new Dictionary<string, object>();
            imagePart["type"] = "image_url";
            imagePart["image_url"] = imageUrl;

            object[] content = new object[2];
            content[0] = textPart;
            content[1] = imagePart;

            string answer = await PostChatAsync(cfg.Model, content, cfg, 0.1, 1200, r).ConfigureAwait(false);
            if (!r.Ok) return r;
            if (string.IsNullOrWhiteSpace(answer)) { r.Error = "模型返回了空内容。"; return r; }

            string json = ExtractJson(answer);
            try
            {
                Dictionary<string, object> obj = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                r.Ja = Pick(obj, "ja", "原文", "japanese", "text", "jp");
                r.Zh = Pick(obj, "zh", "译文", "chinese", "translation", "cn");
                if (string.IsNullOrWhiteSpace(r.Ja) && string.IsNullOrWhiteSpace(r.Zh))
                {
                    if (!answer.TrimStart().StartsWith("{")) r.Zh = answer.Trim();
                }
            }
            catch
            {
                r.Zh = answer.Trim();
            }
            r.Zh = FixQuotes(r.Ja, r.Zh);
            r.Zh = AlignFormat(r.Ja, r.Zh);
            return r;
        }

        // ---------------- 双模型 ----------------
        static async Task<TranslationResult> TwoStepAsync(Bitmap bmp, AppConfig cfg)
        {
            TranslationResult r = new TranslationResult();
            string ocrModel = string.IsNullOrWhiteSpace(cfg.OcrModel) ? cfg.Model : cfg.OcrModel;
            if (!CheckConfig(cfg, ocrModel, r)) return r;
            if (string.IsNullOrWhiteSpace(cfg.TransModel)) { r.Error = "双模型模式缺少翻译模型。"; return r; }

            // 第一步：只识别日文
            string b64 = EncodeJpeg(bmp, 90);

            var textPart = new Dictionary<string, object>();
            textPart["type"] = "text";
            textPart["text"] = "请逐行读取图片中的日文文字，严格保留原始换行与每行行首的符号"
                + "（「」『』、・、-、※、数字编号等），不要合并成一段。"
                + "只输出日文原文，不要翻译、不要解释、不要编号、不要加引号。若图中没有日文，输出空内容。";

            var imageUrl = new Dictionary<string, object>();
            imageUrl["url"] = "data:image/jpeg;base64," + b64;

            var imagePart = new Dictionary<string, object>();
            imagePart["type"] = "image_url";
            imagePart["image_url"] = imageUrl;

            object[] content = new object[2];
            content[0] = textPart;
            content[1] = imagePart;

            string ja = await PostChatAsync(ocrModel, content, cfg, 0.1, 600, r).ConfigureAwait(false);
            if (!r.Ok) return r;

            ja = CleanText(ja);
            r.Ja = ja;
            if (ja.Length == 0)
            {
                r.Zh = "";
                return r;
            }

            // 第二步：纯文本翻译
            string zh;
            if (IsMtModel(cfg.TransModel))
            {
                // Qwen-MT 是翻译专用模型：直接给原文 + translation_options，
                // 不要加任何指令（它会把指令当正文翻译）
                var mt = new Dictionary<string, object>();
                mt["role"] = "user";
                mt["content"] = ja;
                object[] mtMessages = new object[1];
                mtMessages[0] = mt;
                zh = await PostChatAsync(cfg.TransModel, null, cfg, 0.1, 800, r, mtMessages, true).ConfigureAwait(false);
            }
            else
            {
                var msg = new Dictionary<string, object>();
                msg["role"] = "user";
                msg["content"] = "把下面的日文逐行翻译成自然、口语化的简体中文。"
                    + "译文行数必须与原文完全一致、逐行对应，行首符号（「」『』、・、-、※、编号等）原样保留。"
                    + "只输出中文译文，不要解释、不要加引号、不要保留日文。\r\n\r\n日文：\r\n" + ja;

                object[] messages = new object[1];
                messages[0] = msg;
                zh = await PostChatAsync(cfg.TransModel, null, cfg, 0.2, 800, r, messages, false).ConfigureAwait(false);
            }
            if (!r.Ok) return r;

            r.Zh = CleanText(zh);
            r.Zh = FixQuotes(r.Ja, r.Zh);
            r.Zh = AlignFormat(r.Ja, r.Zh);
            return r;
        }

        // ---------------- 通用请求 ----------------
        static bool CheckConfig(AppConfig cfg, string model, TranslationResult r)
        {
            if (string.IsNullOrWhiteSpace(cfg.ApiKey)) { r.Error = "未配置 API Key：请打开设置填写。"; return false; }
            if (string.IsNullOrWhiteSpace(cfg.ApiBase)) { r.Error = "未配置接口地址。"; return false; }
            if (string.IsNullOrWhiteSpace(model)) { r.Error = "未配置模型名称。"; return false; }
            return true;
        }

        static bool IsMtModel(string model)
        {
            return !string.IsNullOrWhiteSpace(model)
                && model.IndexOf("qwen-mt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// content 为多模态内容数组；若传 null 则使用 messages。
        /// </summary>
        static async Task<string> PostChatAsync(string model, object[] content, AppConfig cfg,
            double temperature, int maxTokens, TranslationResult r, object[] messages = null,
            bool translationOptions = false)
        {
            if (messages == null)
            {
                var userMessage = new Dictionary<string, object>();
                userMessage["role"] = "user";
                userMessage["content"] = content;
                messages = new object[1];
                messages[0] = userMessage;
            }

            var body = new Dictionary<string, object>();
            body["model"] = model;
            body["messages"] = messages;
            body["temperature"] = temperature;
            body["max_tokens"] = maxTokens;
            body["stream"] = false;
            // 通义的 Qwen3 系列默认会先输出大段思考过程（几十秒、几千 token），
            // 这里显式关闭；其他服务商不支持该字段，故只对 DashScope 添加。
            if (cfg.ApiBase.IndexOf("dashscope", StringComparison.OrdinalIgnoreCase) >= 0
                || cfg.ApiBase.IndexOf("aliyuncs", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                body["enable_thinking"] = false;
            }

            // Qwen-MT 专用翻译参数
            if (translationOptions)
            {
                var opt = new Dictionary<string, object>();
                opt["source_lang"] = "Japanese";
                opt["target_lang"] = "Chinese";
                body["translation_options"] = opt;
            }

            string url = cfg.ApiBase.TrimEnd('/') + "/chat/completions";
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.ApiKey.Trim());

            JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            req.Content = new StringContent(ser.Serialize(body), Encoding.UTF8, "application/json");

            string text;
            try
            {
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        r.Error = FriendlyError((int)resp.StatusCode, text) + "（模型 " + model + "）";
                        return "";
                    }
                }
            }
            catch (TaskCanceledException) { r.Error = "请求超时（90 秒）。"; return ""; }
            catch (Exception ex) { r.Error = "网络错误：" + ex.Message; return ""; }

            try
            {
                string answer = ReadContent(text, ser);
                if (string.IsNullOrWhiteSpace(answer)) r.Error = "模型返回了空内容（" + model + "）。";
                return answer;
            }
            catch (Exception ex)
            {
                r.Error = "响应解析失败：" + ex.Message;
                return "";
            }
        }

        static string CleanText(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl >= 0) s = s.Substring(nl + 1);
                int tail = s.LastIndexOf("```", StringComparison.Ordinal);
                if (tail >= 0) s = s.Substring(0, tail);
                s = s.Trim();
            }
            if (s.Length >= 2)
            {
                char a = s[0];
                char b = s[s.Length - 1];
                if ((a == '"' && b == '"') || (a == '“' && b == '”')) s = s.Substring(1, s.Length - 2).Trim();
            }
            return NormalizeNewlines(s);
        }

        /// <summary>把字面 \n、\r\n 统一成真实换行</summary>
        static string NormalizeNewlines(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
            return s;
        }

        static bool IsPrefixChar(char c)
        {
            return char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsDigit(c);
        }

        static string ExtractPrefix(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            int i = 0;
            while (i < line.Length && i < 8 && IsPrefixChar(line[i])) i++;
            return line.Substring(0, i);
        }

        /// <summary>原文用了「」，译文却被转成了中文引号时，改回来</summary>
        static string FixQuotes(string ja, string zh)
        {
            if (string.IsNullOrEmpty(ja) || string.IsNullOrEmpty(zh)) return zh;
            if (ja.IndexOf('「') >= 0 && zh.IndexOf('“') >= 0)
                zh = zh.Replace('“', '「').Replace('”', '」');
            return zh;
        }

        /// <summary>
        /// 行数一致时，把原文每行开头的符号（「」『』、・、※、编号等）补到译文对应行，
        /// 避免模型漏掉格式。
        /// </summary>
        static string AlignFormat(string ja, string zh)
        {
            if (string.IsNullOrEmpty(zh)) return zh;
            string j = NormalizeNewlines(ja ?? "");
            string z = NormalizeNewlines(zh);

            string[] jaLines = j.Split('\n');
            string[] zhLines = z.Split('\n');
            if (jaLines.Length != zhLines.Length) return z;

            for (int i = 0; i < zhLines.Length; i++)
            {
                if (zhLines[i].Trim().Length == 0) continue;
                string p = ExtractPrefix(jaLines[i]);
                if (p.Length == 0 || p.Trim().Length == 0) continue;
                if (zhLines[i].StartsWith(p)) continue;
                zhLines[i] = p + zhLines[i].TrimStart();
            }
            return string.Join("\n", zhLines);
        }

        static string Pick(Dictionary<string, object> obj, params string[] keys)
        {
            foreach (string k in keys)
            {
                object v;
                if (obj.TryGetValue(k, out v) && v != null) return Convert.ToString(v).Trim();
            }
            foreach (string k in keys)
            {
                foreach (KeyValuePair<string, object> kv in obj)
                {
                    if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        return Convert.ToString(kv.Value).Trim();
                }
            }
            return "";
        }

        static string ReadContent(string responseText, JavaScriptSerializer ser)
        {
            Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(responseText);
            if (root == null) return "";

            object choicesObj;
            if (!root.TryGetValue("choices", out choicesObj) || choicesObj == null) return "";

            object firstObj = FirstItem(choicesObj);
            Dictionary<string, object> first = firstObj as Dictionary<string, object>;
            if (first == null) return "";

            object msgObj;
            if (!first.TryGetValue("message", out msgObj) || msgObj == null) return "";
            Dictionary<string, object> msg = msgObj as Dictionary<string, object>;
            if (msg == null) return "";

            object c;
            if (!msg.TryGetValue("content", out c) || c == null) return "";

            string s = c as string;
            if (s != null) return s;

            StringBuilder sb = new StringBuilder();
            System.Collections.IEnumerable list = c as System.Collections.IEnumerable;
            if (list != null)
            {
                foreach (object item in list)
                {
                    Dictionary<string, object> d = item as Dictionary<string, object>;
                    if (d != null && d.ContainsKey("text")) sb.Append(Convert.ToString(d["text"]));
                }
            }
            if (sb.Length > 0) return sb.ToString();
            return Convert.ToString(c);
        }

        static object FirstItem(object collection)
        {
            System.Collections.IEnumerable list = collection as System.Collections.IEnumerable;
            if (list == null || collection is string) return null;
            foreach (object item in list) return item;
            return null;
        }

        static string ExtractJson(string s)
        {
            s = s.Trim();
            int a = s.IndexOf('{');
            int b = s.LastIndexOf('}');
            if (a >= 0 && b > a) return s.Substring(a, b - a + 1);
            return s;
        }

        static string FriendlyError(int code, string body)
        {
            string detail = Summarize(body);
            switch (code)
            {
                case 400:
                    if (detail.IndexOf("translation_options", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "该模型不支持文本翻译（多为实时语音/翻译专用模型），请在设置里换成 qwen3.5-flash、deepseek-v4-pro 等文本模型。" + detail;
                    return "请求被拒绝（400）：多为模型不支持图片或参数不符。" + detail;
                case 401: return "API Key 无效或已过期（401）。" + detail;
                case 403: return "无权访问该模型（403）：请确认已开通/实名。" + detail;
                case 404: return "接口地址或模型名不存在（404）。请确认 Base URL 与模型名；通义千问账号下可用的视觉模型有 qwen3-vl-plus、qwen3.5-omni-plus 等。" + detail;
                case 413: return "图片过大（413）：请缩小选区后重试。";
                case 429: return "请求过于频繁或额度用尽（429）。" + detail;
                case 500:
                case 502:
                case 503:
                case 504: return "服务端暂时不可用（" + code + "），请稍后重试。" + detail;
                default: return "接口返回错误（" + code + "）。" + detail;
            }
        }

        static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(body);
                object errObj;
                if (d.TryGetValue("error", out errObj))
                {
                    Dictionary<string, object> e = errObj as Dictionary<string, object>;
                    if (e != null && e.ContainsKey("message")) return " " + Convert.ToString(e["message"]);
                    return " " + Convert.ToString(errObj);
                }
                if (d.ContainsKey("message")) return " " + Convert.ToString(d["message"]);
            }
            catch { }
            string t = body.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > 220) t = t.Substring(0, 220) + "…";
            return " " + t;
        }
    }
}
