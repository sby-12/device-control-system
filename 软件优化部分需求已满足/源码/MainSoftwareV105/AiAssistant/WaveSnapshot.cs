using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainSoftwareV100
{
    /// <summary>
    /// 波形快照仓库（静态类，不用 new 直接用）。
    /// 职责：主窗口每次收到示波器一帧波形时，在这里存一份副本；
    ///       AI 助手通过工具读取副本做统计计算，不影响正常刷图。
    /// 每帧 1000 个 float 约 4KB，四通道一共 16KB，代价可忽略。
    /// </summary>
    public static class WaveSnapshot
    {
        //每个通道一份：波形点、时间轴参数、存帧时间
        public class ChannelFrame
        {
            public float[] Data = new float[0];      //波形点（伏）
            public double XIncrement = 0;            //相邻点的时间间隔（秒）
            public double XOrigin = 0;               //第0点的时刻（秒）
            public DateTime Time = DateTime.MinValue;//存帧时间
        }

        private static readonly ChannelFrame[] _frames = new ChannelFrame[4]
        {
            new ChannelFrame(), new ChannelFrame(), new ChannelFrame(), new ChannelFrame()
        };

        //===== 深存槽（2026-10-31 双槽位改造）=====
        //普通刷新写 _frames，深存储保存写 _deepFrames，互不覆盖。
        //Get() 优先返回深存槽：参数提取界面和 AI 助手自动拿到最精细的数据，
        //直到操作员点"丢弃深存数据"（ClearDeep）或下次深存储覆盖。
        private static readonly ChannelFrame[] _deepFrames = new ChannelFrame[4]
        {
            new ChannelFrame(), new ChannelFrame(), new ChannelFrame(), new ChannelFrame()
        };

        /// <summary>存一帧深存储波形（由主窗口深度保存完成回调调用，不进历史趋势）。</summary>
        public static void StoreDeep(int ch, float[] data, double xIncrement, double xOrigin)
        {
            if (ch < 0 || ch > 3 || data == null || data.Length == 0) return;
            lock (_lock)
            {
                _deepFrames[ch].Data = (float[])data.Clone();
                _deepFrames[ch].XIncrement = xIncrement;
                _deepFrames[ch].XOrigin = xOrigin;
                _deepFrames[ch].Time = DateTime.Now;
            }
        }

        /// <summary>该通道是否有深存储数据。</summary>
        public static bool HasDeep(int ch)
        {
            if (ch < 0 || ch > 3) return false;
            lock (_lock) { return _deepFrames[ch].Data.Length > 0; }
        }

        /// <summary>丢弃全部通道的深存储数据（之后 Get 退回普通快照）。</summary>
        public static void ClearDeep()
        {
            lock (_lock)
            {
                for (int c = 0; c < 4; c++) _deepFrames[c].Data = new float[0];
            }
        }

        /// <summary>深存储数据状态摘要（点数+保存时间），供参数提取界面顶部状态条显示。</summary>
        public static string DeepStatusText()
        {
            lock (_lock)
            {
                int n = 0, pts = 0;
                DateTime t = DateTime.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    if (_deepFrames[c].Data.Length > 0)
                    {
                        n++;
                        if (_deepFrames[c].Data.Length > pts) pts = _deepFrames[c].Data.Length;
                        if (_deepFrames[c].Time > t) t = _deepFrames[c].Time;
                    }
                }
                if (n == 0) return null;
                return "深存储数据已载入（" + n + " 通道 × 最多 " + pts + " 点，保存于 " + t.ToString("HH:mm:ss") + "）：分析优先使用深存储波形";
            }
        }

        /// <summary>一帧的快速统计（存帧时顺便算出，供历史趋势对比）。</summary>
        public class FrameStat
        {
            public DateTime Time;
            public double Vmax, Vmin, Vpp, Avg, Rms, Freq;
        }

        //每通道最近10帧的统计环形缓冲（Queue实现，满了丢最旧）
        private const int HistoryKeep = 10;
        private static readonly Queue<FrameStat>[] _history = new Queue<FrameStat>[4]
        {
            new Queue<FrameStat>(), new Queue<FrameStat>(), new Queue<FrameStat>(), new Queue<FrameStat>()
        };

        private static readonly object _lock = new object();

        /// <summary>
        /// 存一帧。ch 取值 0~3（对应 CH1~CH4）。由主窗口波形回调调用。
        /// 存帧同时算一份快速统计进历史缓冲（1000点一遍循环，代价可忽略），
        /// 让AI能回答"过冲是不是比刚才大了"这类趋势问题。
        /// </summary>
        public static void Store(int ch, float[] data, double xIncrement, double xOrigin)
        {
            if (ch < 0 || ch > 3 || data == null || data.Length == 0) return;
            FrameStat stat = QuickStats(data, xIncrement);
            lock (_lock)
            {
                //复制一份再存，防止后续被别的线程改动
                _frames[ch].Data = (float[])data.Clone();
                _frames[ch].XIncrement = xIncrement;
                _frames[ch].XOrigin = xOrigin;
                _frames[ch].Time = DateTime.Now;

                _history[ch].Enqueue(stat);
                while (_history[ch].Count > HistoryKeep) _history[ch].Dequeue();
            }
        }

        /// <summary>对一帧算快速统计（最大/最小/峰峰值/平均/有效值/过零法估频率）。</summary>
        private static FrameStat QuickStats(float[] data, double xIncrement)
        {
            int n = data.Length;
            double sum = 0, sumSq = 0, vmax = double.MinValue, vmin = double.MaxValue;
            foreach (float v in data)
            {
                sum += v; sumSq += (double)v * v;
                if (v > vmax) vmax = v;
                if (v < vmin) vmin = v;
            }
            double avg = sum / n;

            int crossings = 0;
            for (int i = 1; i < n; i++)
                if (data[i - 1] < avg && data[i] >= avg) crossings++;
            double duration = (xIncrement > 0) ? n * xIncrement : 0;
            double freq = (crossings >= 2 && duration > 0) ? (crossings - 1) / duration : 0;

            return new FrameStat
            {
                Time = DateTime.Now,
                Vmax = vmax, Vmin = vmin, Vpp = vmax - vmin,
                Avg = avg, Rms = Math.Sqrt(sumSq / n), Freq = freq
            };
        }

        /// <summary>
        /// 历史趋势摘要：列出该通道最近若干帧的关键统计（旧→新），并给出最新帧相对上一帧的变化量。
        /// AI用它回答"波形有没有变大/漂移/频率变了没有"这类对比问题。
        /// </summary>
        public static string HistoryText(int ch)
        {
            FrameStat[] snaps;
            lock (_lock) { snaps = _history[ch].ToArray(); }
            if (snaps.Length == 0) return "通道" + (ch + 1) + "还没有波形数据（请先在主界面启动刷新）。";

            StringBuilder sb = new StringBuilder();
            sb.Append("通道" + (ch + 1) + "最近" + snaps.Length + "帧统计（旧→新，间隔约等于波形刷新周期）：\n");
            for (int i = 0; i < snaps.Length; i++)
            {
                FrameStat s = snaps[i];
                sb.Append(s.Time.ToString("HH:mm:ss"))
                  .Append("  峰峰值=").Append(s.Vpp.ToString("0.###")).Append("V")
                  .Append("，最大=").Append(s.Vmax.ToString("0.###")).Append("V")
                  .Append("，平均=").Append(s.Avg.ToString("0.###")).Append("V")
                  .Append("，有效值=").Append(s.Rms.ToString("0.###")).Append("V");
                if (s.Freq > 0) sb.Append("，频率≈").Append(s.Freq.ToString("0.###")).Append("Hz");
                sb.Append("\n");
            }

            if (snaps.Length >= 2)
            {
                FrameStat last = snaps[snaps.Length - 1], prev = snaps[snaps.Length - 2];
                sb.Append("最新帧相比上一帧：峰峰值变化 ").Append((last.Vpp - prev.Vpp).ToString("+0.###;-0.###;0")).Append("V");
                sb.Append("，平均值变化 ").Append((last.Avg - prev.Avg).ToString("+0.###;-0.###;0")).Append("V");
                if (last.Freq > 0 && prev.Freq > 0)
                    sb.Append("，频率变化 ").Append((last.Freq - prev.Freq).ToString("+0.###;-0.###;0")).Append("Hz");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 取一帧副本（浅拷贝数组，已加锁安全），没存过返回null。
        /// 优先返回深存槽（有深存储数据时），否则返回普通实时快照。
        /// </summary>
        public static ChannelFrame Get(int ch)
        {
            if (ch < 0 || ch > 3) return null;
            lock (_lock)
            {
                ChannelFrame src = _deepFrames[ch].Data.Length > 0 ? _deepFrames[ch] : _frames[ch];
                if (src.Data.Length == 0) return null;
                return new ChannelFrame()
                {
                    Data = (float[])src.Data.Clone(),
                    XIncrement = src.XIncrement,
                    XOrigin = src.XOrigin,
                    Time = src.Time
                };
            }
        }

        /// <summary>
        /// 公式计算器：对一帧做统计，返回"人话摘要"（AI直接拿它描述波形，不用看1000个点）。
        /// 频率用过零法估算，只对近似周期信号有效。
        /// 2026-10-17 参考GitHub开源方案扩充：峰值因数、波形因数（NWaves/endolith波形分析套路）、
        /// 纹波、标准差、上升/下降时间（10%-90%）、占空比、削顶检测。
        /// </summary>
        public static string StatsText(int ch)
        {
            ChannelFrame f = Get(ch);
            if (f == null) return "通道" + (ch + 1) + "还没有波形数据（请先在主界面启动刷新）。";

            double sum = 0, sumSq = 0, sumAbs = 0, vmax = double.MinValue, vmin = double.MaxValue;
            foreach (float v in f.Data)
            {
                sum += v; sumSq += (double)v * v; sumAbs += Math.Abs(v);
                if (v > vmax) vmax = v;
                if (v < vmin) vmin = v;
            }
            int n = f.Data.Length;
            double avg = sum / n;
            double rms = Math.Sqrt(sumSq / n);
            double vpp = vmax - vmin;
            double ripple = Math.Sqrt(Math.Max(0, rms * rms - avg * avg));   //去直流后的有效值=纹波
            double std = StdOf(f.Data, avg);
            double crest = (rms > 0) ? Math.Max(Math.Abs(vmax), Math.Abs(vmin)) / rms : 0;  //峰值因数
            //波形因数 = 有效值 / 整流平均值（经典定义）。要用|v|的平均值，
            //不能用带符号平均值——对称正弦的平均值≈0，会除出荒谬结果
            double meanAbs = sumAbs / n;
            double form = (meanAbs > 0) ? rms / meanAbs : 0;

            //过零法估频率：统计上升过零次数（阈值取平均值）
            int crossings = 0;
            for (int i = 1; i < n; i++)
                if (f.Data[i - 1] < avg && f.Data[i] >= avg) crossings++;

            double duration = (f.XIncrement > 0) ? n * f.XIncrement : 0;
            double freq = (crossings >= 2 && duration > 0) ? (crossings - 1) / duration : 0;

            StringBuilder sb = new StringBuilder();
            sb.Append("通道" + (ch + 1) + "波形统计（共" + n + "点");
            if (duration > 0) sb.Append("，时长约" + duration.ToString("0.###") + "秒");
            sb.Append("，存帧时间" + f.Time.ToString("HH:mm:ss") + "）：");
            sb.Append("最大值=" + vmax.ToString("0.###") + "V，最小值=" + vmin.ToString("0.###") + "V，");
            sb.Append("峰峰值=" + vpp.ToString("0.###") + "V，平均值=" + avg.ToString("0.###") + "V，有效值=" + rms.ToString("0.###") + "V，");
            sb.Append("纹波(去直流有效值)=" + ripple.ToString("0.###") + "V，标准差=" + std.ToString("0.###") + "，");
            sb.Append("峰值因数=" + crest.ToString("0.##") + "（正弦波应≈1.414），波形因数=" + form.ToString("0.##") + "（正弦波应≈1.11）");

            //削顶/削底检测：贴住最高/最低值的点占比超过2%即怀疑饱和削顶
            int clipTop = 0, clipBot = 0;
            double topBand = vmax - 0.01 * vpp, botBand = vmin + 0.01 * vpp;
            foreach (float v in f.Data)
            {
                if (v >= topBand) clipTop++;
                if (v <= botBand) clipBot++;
            }
            if (vpp > 0 && (clipTop > 0.02 * n || clipBot > 0.02 * n))
                sb.Append("；注意：可能有" + ((clipTop > clipBot) ? "顶部削顶(饱和)" : "底部削底") + "（贴边点占" +
                    (100.0 * Math.Max(clipTop, clipBot) / n).ToString("0") + "%），量程可能偏小或信号已饱和");

            //上升/下降时间（10%→90%、90%→10%的最短时间，step/脉冲类波形才有意义）
            if (vpp > 0 && f.XIncrement > 0)
            {
                double v10 = vmin + 0.1 * vpp, v90 = vmin + 0.9 * vpp;
                double rise = EdgeTime(f.Data, v10, v90, true) * f.XIncrement;
                double fall = EdgeTime(f.Data, v90, v10, false) * f.XIncrement;
                if (rise > 0) sb.Append("；最快上升沿时间(10%~90%)≈" + RiseFmt(rise));
                if (fall > 0) sb.Append("；最快下降沿时间(90%~10%)≈" + RiseFmt(fall));
            }

            //占空比（以平均值为阈值，仅脉冲/方波类有意义）
            int above = 0;
            foreach (float v in f.Data) if (v > avg) above++;
            sb.Append("；高于平均值的时间占比=" + (100.0 * above / n).ToString("0") + "%（脉冲/方波类波形即近似占空比）");

            if (freq > 0) sb.Append("；估算频率≈" + freq.ToString("0.###") + "Hz（过零法，近似周期信号才准）");
            return sb.ToString();
        }

        /// <summary>
        /// 频谱摘要：Hann窗 + 1024点FFT（Cooley-Tukey，参考FftSharp思路手写，零外部依赖）。
        /// 返回主频（抛物线插值精化）、前5个谱峰、THD估算（2~5次谐波）。AI用它判断谐波失真、振铃频率、噪声成分。
        /// </summary>
        public static string SpectrumText(int ch)
        {
            ChannelFrame f = Get(ch);
            if (f == null) return "通道" + (ch + 1) + "没有数据。";
            if (f.XIncrement <= 0) return "通道" + (ch + 1) + "缺少时间轴参数（XIncrement未知），无法算频率。";

            int n = 1024;                        //1000点补零到1024
            double fs = 1.0 / f.XIncrement;      //采样率
            double[] re = new double[n], im = new double[n];
            int len = Math.Min(n, f.Data.Length);
            //Hann窗 + 去直流（FFT前常规操作，参考FftSharp/windows）
            double mean = f.Data.Take(len).Average(v => (double)v);
            for (int i = 0; i < len; i++)
            {
                double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (len - 1)));
                re[i] = (f.Data[i] - mean) * w;
            }

            Fft(re, im);

            //单边幅度谱（Hann窗相干增益0.5，幅值修正约×2）
            int half = n / 2;
            double[] mag = new double[half];
            for (int k = 0; k < half; k++)
                mag[k] = 4.0 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / n;

            //找主频（跳过直流附近，k>=3）
            int k0 = 3;
            for (int k = 3; k < half; k++) if (mag[k] > mag[k0]) k0 = k;
            //抛物线插值精化峰值频率（endolith波形分析的插值套路）
            double delta = 0;
            if (k0 > 3 && k0 < half - 1)
            {
                double yl = mag[k0 - 1], yc = mag[k0], yr = mag[k0 + 1];
                double den = yl - 2 * yc + yr;
                if (Math.Abs(den) > 1e-12) delta = 0.5 * (yl - yr) / den;
                if (delta < -1 || delta > 1) delta = 0;
            }
            double fundFreq = (k0 + delta) * fs / n;
            double fundAmp = mag[k0];

            //前5个谱峰（简单峰值挑选：局部极大，间隔至少3个bin）
            List<int> peaks = new List<int>();
            for (int k = 4; k < half - 1 && peaks.Count < 5; k++)
            {
                if (mag[k] > mag[k - 1] && mag[k] >= mag[k + 1] && mag[k] > 0.05 * fundAmp)
                {
                    if (peaks.Count == 0 || k - peaks[peaks.Count - 1] > 3) peaks.Add(k);
                }
            }

            //THD估算：2~5次谐波（取最靠近理论位置的bin）
            double harmSq = 0;
            for (int h = 2; h <= 5; h++)
            {
                int kh = (int)Math.Round(h * (k0 + delta));
                if (kh < half) harmSq += mag[kh] * mag[kh];
            }
            double thd = (fundAmp > 0) ? Math.Sqrt(harmSq) / fundAmp : 0;

            StringBuilder sb = new StringBuilder();
            sb.Append("通道" + (ch + 1) + "频谱分析（" + n + "点FFT，采样率≈" + FsFmt(fs) + "，频率分辨率≈" + FsFmt(fs / n) + "）：");
            sb.Append("主频≈" + FsFmt(fundFreq) + "（幅值约" + fundAmp.ToString("0.###") + "V），THD(2~5次谐波)≈" + (100 * thd).ToString("0.0") + "%");
            sb.Append("；主要谱峰：");
            foreach (int k in peaks)
                sb.Append(FsFmt(k * fs / n) + "处" + mag[k].ToString("0.###") + "V，");
            sb.Append("。说明：幅值为Hann窗估算值，频率分辨率受采样点数限制；振铃、噪声尖峰会在谱峰里体现");
            return sb.ToString();
        }

        //============ 内部小工具 ============

        private static double StdOf(float[] d, double mean)
        {
            double s = 0;
            foreach (float v in d) s += (v - mean) * (v - mean);
            return Math.Sqrt(s / d.Length);
        }

        /// <summary>找最短的"从levelA穿越到levelB"的采样间隔数（用于10%~90%时间）。rising=true找上升沿。</summary>
        private static int EdgeTime(float[] d, double levelA, double levelB, bool rising)
        {
            int n = d.Length, best = int.MaxValue;
            for (int i = 1; i < n; i++)
            {
                bool crossA = rising ? (d[i - 1] < levelA && d[i] >= levelA)
                                     : (d[i - 1] > levelA && d[i] <= levelA);
                if (!crossA) continue;
                for (int j = i + 1; j < n && j - i < best; j++)
                {
                    bool crossB = rising ? (d[j] >= levelB) : (d[j] <= levelB);
                    if (crossB) { best = j - i; break; }
                }
            }
            return (best == int.MaxValue) ? -1 : best;
        }

        /// <summary>就地FFT（Cooley-Tukey迭代版），输入长度必须是2的幂。参考FftSharp思路手写，避免外部依赖。</summary>
        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            //位反转重排
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j |= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            //蝶形运算
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cwr = 1, cwi = 0;
                    for (int j = 0; j < len / 2; j++)
                    {
                        int a = i + j, b = i + j + len / 2;
                        double tr = re[b] * cwr - im[b] * cwi;
                        double ti = re[b] * cwi + im[b] * cwr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double nwr = cwr * wr - cwi * wi, nwi = cwr * wi + cwi * wr;
                        cwr = nwr; cwi = nwi;
                    }
                }
            }
        }

        private static string FsFmt(double hz)
        {
            if (hz >= 1e6) return (hz / 1e6).ToString("0.###") + "MHz";
            if (hz >= 1e3) return (hz / 1e3).ToString("0.###") + "kHz";
            return hz.ToString("0.###") + "Hz";
        }

        private static string RiseFmt(double sec)
        {
            if (sec >= 1) return sec.ToString("0.###") + "秒";
            if (sec >= 1e-3) return (sec / 1e-3).ToString("0.###") + "ms";
            if (sec >= 1e-6) return (sec / 1e-6).ToString("0.###") + "us";
            return (sec / 1e-9).ToString("0.###") + "ns";
        }

        /// <summary>
        /// 抽稀输出：等间隔取最多 maxPoints 个点，给 AI 看形状用（文本序列，不给全量1000点省token）
        /// </summary>
        public static string DownsampleText(int ch, int maxPoints)
        {
            ChannelFrame f = Get(ch);
            if (f == null) return "通道" + (ch + 1) + "没有数据。";
            if (maxPoints < 10) maxPoints = 10;
            if (maxPoints > 500) maxPoints = 500;
            int n = f.Data.Length;
            int step = Math.Max(1, n / maxPoints);
            StringBuilder sb = new StringBuilder("通道" + (ch + 1) + "抽稀波形（每" + step + "点取1个）：");
            for (int i = 0; i < n; i += step)
                sb.Append(f.Data[i].ToString("0.###") + ",");
            return sb.ToString().TrimEnd(',');
        }
    }
}
