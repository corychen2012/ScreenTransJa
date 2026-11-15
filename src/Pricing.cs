using System;
using System.Collections.Generic;

namespace ScreenTrans
{
    /// <summary>模型参考价（人民币 / 千 tokens）</summary>
    public class ModelPrice
    {
        public string Id = "";
        public double InputPerK;
        public double OutputPerK;

        public ModelPrice(string id, double inputPerK, double outputPerK)
        {
            Id = id;
            InputPerK = inputPerK;
            OutputPerK = outputPerK;
        }
    }

    /// <summary>
    /// 价格来源：
    /// 1) 接口 /models 若返回 pricing 字段（如 OpenRouter 风格），优先使用；
    /// 2) 否则查内置参考价表（按 1 USD≈7.2 CNY 粗算，仅供比价，以官网为准）。
    /// </summary>
    public static class Pricing
    {
        static readonly Dictionary<string, ModelPrice> Table =
            new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase);

        static Pricing()
        {
            // 通义千问（DashScope）
            Table["qwen3-vl-plus"] = new ModelPrice("qwen3-vl-plus", 0.0015, 0.0045);
            Table["qwen3.5-omni-plus"] = new ModelPrice("qwen3.5-omni-plus", 0.0020, 0.0060);
            Table["qwen3.5-omni-flash"] = new ModelPrice("qwen3.5-omni-flash", 0.0005, 0.0015);
            Table["qwen3.5-omni-flash-realtime"] = new ModelPrice("qwen3.5-omni-flash-realtime", 0.0008, 0.0024);
            Table["qwen-vl-ocr"] = new ModelPrice("qwen-vl-ocr", 0.0030, 0.0030);
            Table["qwen3.5-ocr"] = new ModelPrice("qwen3.5-ocr", 0.0030, 0.0030);
            Table["qwen3.5-flash"] = new ModelPrice("qwen3.5-flash", 0.0003, 0.0006);
            Table["qwen3.5-plus"] = new ModelPrice("qwen3.5-plus", 0.0012, 0.0036);
            Table["qwen-turbo"] = new ModelPrice("qwen-turbo", 0.0003, 0.0006);
            Table["qwen-plus"] = new ModelPrice("qwen-plus", 0.0008, 0.0020);
            Table["qwen-flash"] = new ModelPrice("qwen-flash", 0.0003, 0.0006);

            // 智谱
            Table["glm-4v-flash"] = new ModelPrice("glm-4v-flash", 0, 0); // 免费
            Table["glm-4v-plus"] = new ModelPrice("glm-4v-plus", 0.0100, 0.0100);
            Table["glm-4.5v"] = new ModelPrice("glm-4.5v", 0.0050, 0.0050);

            // OpenAI
            Table["gpt-4o-mini"] = new ModelPrice("gpt-4o-mini", 0.0011, 0.0044);
            Table["gpt-4o"] = new ModelPrice("gpt-4o", 0.0180, 0.0720);
            Table["gpt-4.1-mini"] = new ModelPrice("gpt-4.1-mini", 0.0006, 0.0024);
            Table["gpt-4.1"] = new ModelPrice("gpt-4.1", 0.0144, 0.0576);
        }

        /// <summary>查参考价，找不到返回 null</summary>
        public static ModelPrice Find(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            ModelPrice p;
            if (Table.TryGetValue(model.Trim(), out p)) return p;

            // 带日期后缀的模型（如 qwen3-vl-plus-2026-03-15）按前缀匹配
            string m = model.Trim();
            foreach (KeyValuePair<string, ModelPrice> kv in Table)
            {
                if (m.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return new ModelPrice(m, kv.Value.InputPerK, kv.Value.OutputPerK);
            }
            return null;
        }

        /// <summary>列表里显示的一行价格文案</summary>
        public static string ShortText(string model, double? inputUsdPerM, double? outputUsdPerM)
        {
            if (inputUsdPerM.HasValue)
            {
                double inYuan = inputUsdPerM.Value * 7.2 / 1000.0;   // USD/1M → 元/千
                double outYuan = (outputUsdPerM ?? inputUsdPerM.Value) * 7.2 / 1000.0;
                return FormatYuan(inYuan, outYuan);
            }
            ModelPrice p = Find(model);
            if (p == null) return "价格未知";
            if (p.InputPerK <= 0 && p.OutputPerK <= 0) return "免费";
            return FormatYuan(p.InputPerK, p.OutputPerK);
        }

        /// <summary>详情文案（设置页用）</summary>
        public static string DetailText(string model)
        {
            ModelPrice p = Find(model);
            if (p == null) return "价格未知";
            if (p.InputPerK <= 0 && p.OutputPerK <= 0) return "免费";
            return "输入 ¥" + Trim(p.InputPerK) + " / 输出 ¥" + Trim(p.OutputPerK) + " /千tokens";
        }

        static string FormatYuan(double input, double output)
        {
            if (input <= 0 && output <= 0) return "免费";
            return "¥" + Trim(input) + " / ¥" + Trim(output);
        }

        static string Trim(double v)
        {
            if (v >= 1) return v.ToString("0.##");
            if (v >= 0.01) return v.ToString("0.###");
            return v.ToString("0.####");
        }
    }
}
