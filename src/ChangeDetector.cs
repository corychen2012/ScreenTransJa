using System;
using System.Drawing;

namespace ScreenTrans
{
    /// <summary>
    /// 本地"文字是否变化"检测（不依赖任何模型，单帧约 2~4 ms）。
    ///
    /// 思路：
    /// 1. 缩到 160×90 并灰度化；
    /// 2. 算 Sobel 边缘，文字/UI 边线是强边缘，背景渐变、动画粒子基本没有边缘；
    /// 3. 逐像素比较相邻两帧：灰度差够大 且 至少一帧该处是边缘，才记为"文字变化"；
    /// 4. 变化像素占比超过阈值才认为内容真的变了。
    ///
    /// 这样背景轻微变化、光标闪烁、渐变呼吸都不会触发翻译。
    /// </summary>
    public class ChangeDetector
    {
        const int W = 160;
        const int H = 90;
        const int GrayDiff = 34;     // 灰度差阈值
        const int EdgeMin = 26;      // 边缘强度阈值
        const int GridSteps = 5;     // 变化像素需跨越的网格数（避免单点噪声）

        readonly byte[] _prevGray = new byte[W * H];
        readonly byte[] _prevEdge = new byte[W * H];
        readonly byte[] _curGray = new byte[W * H];
        readonly byte[] _curEdge = new byte[W * H];
        bool _hasPrev;

        /// <summary>变化像素占比（0~1），便于调试/显示</summary>
        public double LastRatio { get; private set; }

        /// <summary>触发阈值：占比超过它才算内容变化（0.006~0.03）</summary>
        public double Threshold = 0.012;

        /// <summary>清空基准帧（例如刚翻译完，以新画面为基准）</summary>
        public void Reset()
        {
            _hasPrev = false;
        }

        /// <summary>用指定帧作为基准（翻译完成后调用，避免同一变化重复触发）</summary>
        public void MarkCurrent(Bitmap bmp)
        {
            Update(bmp, true);
        }

        public bool HasTextChanged(Bitmap bmp)
        {
            if (!_hasPrev)
            {
                Update(bmp, true);
                LastRatio = 1.0;
                return true;
            }

            Update(bmp, false);

            int changed = 0;
            int minX = W, minY = H, maxX = -1, maxY = -1;

            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    int i = row + x;
                    int diff = Math.Abs(_curGray[i] - _prevGray[i]);
                    if (diff < GrayDiff) continue;
                    if (Math.Max(_curEdge[i], _prevEdge[i]) < EdgeMin) continue;

                    changed++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            double ratio = (double)changed / (W * H);
            LastRatio = ratio;

            // 更新基准（相邻帧比较）
            Buffer.BlockCopy(_curGray, 0, _prevGray, 0, _curGray.Length);
            Buffer.BlockCopy(_curEdge, 0, _prevEdge, 0, _curEdge.Length);

            if (ratio < Threshold) return false;

            // 变化需要有一定的空间分布，过滤孤立噪点
            if (maxX < 0) return false;
            int spanX = maxX - minX + 1;
            int spanY = maxY - minY + 1;
            if (spanX < W / GridSteps && spanY < H / GridSteps) return false;

            return true;
        }

        void Update(Bitmap bmp, bool copyToPrev)
        {
            using (Bitmap small = new Bitmap(bmp, new Size(W, H)))
            {
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        _curGray[y * W + x] = (byte)((c.R * 77 + c.G * 150 + c.B * 29) >> 8);
                    }
                }

                for (int y = 0; y < H; y++)
                {
                    _curEdge[y * W] = 0;
                    _curEdge[y * W + W - 1] = 0;
                }
                for (int x = 0; x < W; x++)
                {
                    _curEdge[x] = 0;
                    _curEdge[(H - 1) * W + x] = 0;
                }

                for (int y = 1; y < H - 1; y++)
                {
                    for (int x = 1; x < W - 1; x++)
                    {
                        int i = y * W + x;
                        int gx = -_curGray[i - W - 1] - 2 * _curGray[i - W] - _curGray[i - W + 1]
                                 + _curGray[i + W - 1] + 2 * _curGray[i + W] + _curGray[i + W + 1];
                        int gy = -_curGray[i - W - 1] - 2 * _curGray[i - 1] - _curGray[i + W - 1]
                                 + _curGray[i - W + 1] + 2 * _curGray[i + 1] + _curGray[i + W + 1];
                        int v = (Math.Abs(gx) + Math.Abs(gy)) >> 2;
                        _curEdge[i] = (byte)(v > 255 ? 255 : v);
                    }
                }
            }

            if (copyToPrev)
            {
                Buffer.BlockCopy(_curGray, 0, _prevGray, 0, _curGray.Length);
                Buffer.BlockCopy(_curEdge, 0, _prevEdge, 0, _curEdge.Length);
                _hasPrev = true;
            }
        }
    }
}
