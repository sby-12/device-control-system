using System;
using System.Collections.Generic;
using System.Linq;

namespace MainSoftwareV100
{
    /// <summary>
    /// 参数提取的数学基础原语（C#移植自DPT开源项目的核心算法，参考IEC 60747-9定义）。
    /// 全部以"采样点索引"为坐标（等间隔采样），需要时刻时用 索引×dt 换算。
    /// 约定：电压单位伏特、电流单位安培、时间单位秒，能量由调用方换算uJ/mJ。
    /// </summary>
    public static class DptMath
    {
        //========== 平台电平估计 ==========

        /// <summary>分位数（0~1）。先排序再线性插值，等价numpy.percentile。</summary>
        public static double Percentile(double[] data, double pct)
        {
            if (data == null || data.Length == 0) return 0;
            double[] s = (double[])data.Clone();
            Array.Sort(s);
            double pos = pct * (s.Length - 1);
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            if (lo == hi) return s[lo];
            return s[lo] + (s[hi] - s[lo]) * (pos - lo);
        }

        /// <summary>数组某区间的分位数。</summary>
        public static double Percentile(double[] data, int i0, int i1, double pct)
        {
            i0 = Math.Max(0, i0); i1 = Math.Min(data.Length - 1, i1);
            if (i1 <= i0) return data[Math.Max(0, Math.Min(i0, data.Length - 1))];
            double[] seg = new double[i1 - i0 + 1];
            Array.Copy(data, i0, seg, 0, seg.Length);
            return Percentile(seg, pct);
        }

        /// <summary>阈值电平：lo + pct×(hi−lo)。</summary>
        public static double ThresholdValue(double lo, double hi, double pct)
        {
            return lo + pct * (hi - lo);
        }

        //========== 滤波 ==========

        /// <summary>滑动平均平滑（对称窗、边缘截断），window为奇数采样点。
        /// 2026-10-31 修复：原滑动和只累加"过去"半个窗口（恒为 h+1 个元素），
        /// 分母却按完整对称窗 2h+1 算，稳定区输出被压低 (h+1)/(2h+1)（窗3→2/3）。
        /// 改为前缀和实现：分子分母都按 [i-h, i+h] 截断窗计算，恒定信号输出恒为原值。</summary>
        public static double[] Smooth(double[] y, int window)
        {
            if (window < 3) return (double[])y.Clone();
            if (window % 2 == 0) window++;
            int n = y.Length, h = window / 2;
            double[] pre = new double[n + 1];
            for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + y[i];
            double[] r = new double[n];
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - h), hi = Math.Min(n - 1, i + h);
                r[i] = (pre[hi + 1] - pre[lo]) / (hi - lo + 1);
            }
            return r;
        }

        /// <summary>按纳秒指定平滑窗宽，自动换算成采样点数。</summary>
        public static double[] Smooth(double[] y, double dt, double windowNs)
        {
            int w = Math.Max(1, (int)(windowNs * 1e-9 / dt));
            return Smooth(y, w);
        }

        //========== 阈值穿越（核心原语，线性插值） ==========

        /// <summary>
        /// 找波形穿越threshold的第一个时刻，返回双精度采样索引（可带小数，即线性插值结果）。
        /// rising=true找上升穿越，false找下降穿越；startIdx指定从哪个点开始搜。
        /// 找不到返回null。
        /// </summary>
        public static double? CrossingIndex(double[] y, double threshold, bool rising, int startIdx = 0, int endIdx = int.MaxValue)
        {
            int last = Math.Min(y.Length - 1, endIdx);
            for (int i = Math.Max(1, startIdx); i <= last; i++)
            {
                bool cross = rising ? (y[i - 1] <= threshold && y[i] > threshold)
                                    : (y[i - 1] >= threshold && y[i] < threshold);
                if (cross)
                {
                    double y0 = y[i - 1], y1 = y[i];
                    if (Math.Abs(y1 - y0) < 1e-30) return (double)i;
                    double frac = (threshold - y0) / (y1 - y0);
                    return (i - 1) + frac;
                }
            }
            return null;
        }

        //========== 数值积分/斜率 ==========

        /// <summary>梯形法积分 ∫p dt，p与t等长，返回焦耳（当p为瓦特时）。</summary>
        public static double Integrate(double[] t, double[] p, int i0, int i1)
        {
            i0 = Math.Max(0, i0); i1 = Math.Min(t.Length - 1, i1);
            if (i1 <= i0) return 0;
            double sum = 0;
            for (int i = i0 + 1; i <= i1; i++)
                sum += 0.5 * (p[i - 1] + p[i]) * (t[i] - t[i - 1]);
            return sum;
        }

        /// <summary>分数端点梯形积分：a/b 为带小数的采样索引（线性插值穿越点的坐标），
        /// 端点处按 p 的线性插值补部分区间，避免把已插值的穿越点强转整数造成的端点误差。
        /// a/b 为整数时与 Integrate 完全一致；t 需单调递增（如 TimeAxis 产物）。</summary>
        public static double IntegrateFrac(double[] t, double[] p, double a, double b)
        {
            int n = t.Length;
            if (n < 2 || b <= a) return 0;
            a = Math.Max(0, a); b = Math.Min(n - 1.0, b);
            if (b <= a) return 0;
            //前缀积分 F(x)=∫₀ˣ p dt（p 按线性插值，x 可带小数）。
            //任意小数索引处的线性插值取值
            System.Func<double, double> val = x =>
            {
                int i = (int)Math.Floor(x);
                if (i < 0) return p[0];
                if (i >= n - 1) return p[n - 1];
                double f = x - i;
                return p[i] * (1 - f) + p[i + 1] * f;
            };
            double tval(double x)   //时间轴同样按线性插值取
            {
                int i = (int)Math.Floor(x);
                if (i < 0) return t[0];
                if (i >= n - 1) return t[n - 1];
                double f = x - i;
                return t[i] * (1 - f) + t[i + 1] * f;
            }
            System.Func<double, double> prefix = x =>
            {
                if (x <= 0) return 0;
                if (x > n - 1) x = n - 1;
                int k = (int)Math.Floor(x);
                double full = 0;
                for (int j = 1; j <= k; j++)
                    full += 0.5 * (p[j - 1] + p[j]) * (t[j] - t[j - 1]);
                if (x > k)
                    full += 0.5 * (p[k] + val(x)) * (tval(x) - t[k]);
                return full;
            };
            return prefix(b) - prefix(a);
        }

        /// <summary>
        /// 两穿越点间斜率：(thB−thA)/(tB−tA)。
        /// 在[i0,i1]内先找thA的穿越（从段内最值点开始），再找thB的穿越。
        /// 返回每微秒变化量；找不到返回null。
        /// </summary>
        public static double? SlopeBetween(double[] y, double dt, int i0, int i1,
                                           double thA, double thB, bool rising)
        {
            i0 = Math.Max(1, i0); i1 = Math.Min(y.Length - 1, i1);
            if (i1 <= i0) return null;
            //从最值点附近开始搜，避开前导扰动（DPT同款思路）
            int start = i0;
            if (rising)
            {
                int mn = i0; for (int i = i0; i <= i1; i++) if (y[i] < y[mn]) mn = i;
                start = Math.Max(i0, mn - 1);
            }
            else
            {
                int mx = i0; for (int i = i0; i <= i1; i++) if (y[i] > y[mx]) mx = i;
                start = Math.Max(i0, mx - 1);
            }
            double? idxA = CrossingIndex(y, thA, rising, start);
            if (idxA == null) idxA = CrossingIndex(y, thA, rising, i0);
            if (idxA == null) return null;
            double? idxB = CrossingIndex(y, thB, rising, (int)Math.Ceiling(idxA.Value) + 1);
            if (idxB == null || idxB <= idxA) return null;
            double dtus = (idxB.Value - idxA.Value) * dt / 1e-6;   //微秒
            if (Math.Abs(dtus) < 1e-12) return null;
            return Math.Abs(thB - thA) / Math.Abs(dtus);           //每微秒
        }

        /// <summary>生成时间轴（秒）。</summary>
        public static double[] TimeAxis(int n, double dt, double xOrigin)
        {
            double[] t = new double[n];
            for (int i = 0; i < n; i++) t[i] = xOrigin + i * dt;
            return t;
        }

        /// <summary>波形过零/过均值次数（用于粗略判断是否为脉冲类波形）。</summary>
        public static int CountCrossings(double[] y, double level)
        {
            int c = 0;
            for (int i = 1; i < y.Length; i++)
                if ((y[i - 1] < level && y[i] >= level) || (y[i - 1] > level && y[i] <= level)) c++;
            return c;
        }
    }
}
