using System;

namespace MainSoftwareV100
{
    /// <summary>
    /// 反向恢复参数提取（对应UI的"RR"页签）。
    /// 定义来源：IEC 60747-2 二极管反向恢复 / IEC 60747-9 续流恢复能量。
    /// 通道约定：ic=续流二极管电流（或下桥总电流），vce=二极管压降通道，vge=门极脉冲（用于定位第二脉冲开通沿）。
    /// </summary>
    public static class DptRecovery
    {
        public class Result
        {
            public double Irr;        //反向恢复电流峰值 A
            public double Trr;        //反向恢复时间 ns（过零 → 回落到25% Irr）
            public double Vrr;        //恢复期间反向电压峰值 V
            public double DvDt;       //恢复电压斜率 kV/us
            public double DiDt;       //恢复电流斜率 A/us
            public double Err;        //反向恢复能量 uJ
            public double Qrr;        //反向恢复电荷 uC
            public double Pdmax;      //恢复窗峰值功率 kW
            public string Message = "";
            //2026-10-31 逐参数有效状态：含参数名表示该参数不可信/未求得，界面应显示"--"而不是0
            public readonly System.Collections.Generic.HashSet<string> Invalid =
                new System.Collections.Generic.HashSet<string>();
        }

        /// <summary>
        /// preWindowNs/postWindowNs：恢复窗 = 开通沿前/后的纳秒数（原硬编码100/500，现可调，
        /// 脉宽或di/dt差异大的器件可在调用处覆盖）。
        /// </summary>
        public static Result Analyze(double[] vge, double[] vce, double[] ic, double dt,
                                     double preWindowNs = 100.0, double postWindowNs = 500.0)
        {
            Result r = new Result();
            int n = vge == null ? 0 : vge.Length;
            if (n < 100 || vce == null || ic == null || vce.Length != n || ic.Length != n)
            { r.Message = "数据长度不足或三通道长度不一致"; return r; }
            if (!(dt > 0) || double.IsNaN(dt) || double.IsInfinity(dt))
            { r.Message = "采样间隔无效（dt≤0/NaN/∞），拒绝提取，请检查波形来源"; return r; }
            double[] t = DptMath.TimeAxis(n, dt, 0);

            //找第二脉冲开通沿：Vge 50%电平上升穿越（复用开关分析的思路，简化直接搜）
            double lo = DptMath.Percentile(vge, 0.02), hi = DptMath.Percentile(vge, 0.98);
            double thRise = lo + 0.5 * (hi - lo);
            double[] vgeS = DptMath.Smooth(vge, dt, 50);
            double? onIdx = null;
            //先找到第一个下降沿（脉冲1关断），再找其后的上升沿（脉冲2开通）
            double? fall1 = DptMath.CrossingIndex(vgeS, thRise, false, 1);
            if (fall1 != null) onIdx = DptMath.CrossingIndex(vgeS, thRise, true, (int)Math.Ceiling(fall1.Value) + 1);
            if (onIdx == null) { r.Message = "未识别到第二脉冲开通沿"; return r; }

            //恢复窗：开通沿前 preWindowNs 到后 postWindowNs（默认100/500，调用方可覆盖）
            int w0 = Math.Max(0, (int)onIdx.Value - (int)(preWindowNs * 1e-9 / dt));
            int w1 = Math.Min(n - 1, (int)onIdx.Value + (int)(postWindowNs * 1e-9 / dt));

            //Irr：窗内电流极值（上桥为正峰、下桥可能为负，取绝对值最大）
            int peakIdx = w0; double peakAbs = 0;
            for (int i = w0; i <= w1; i++)
            {
                if (Math.Abs(ic[i]) > peakAbs) { peakAbs = Math.Abs(ic[i]); peakIdx = i; }
            }
            if (peakAbs < 0.1) { r.Message = "恢复电流≈0，检查电流通道"; return r; }
            r.Irr = Math.Abs(ic[peakIdx]);
            bool peakNegative = ic[peakIdx] < 0;

            //过零点：峰之前最后一次穿越0（线性插值取小数索引）
            double zeroF = -1;
            for (int i = peakIdx; i > w0; i--)
            {
                if ((ic[i - 1] > 0 && ic[i] <= 0) || (ic[i - 1] < 0 && ic[i] >= 0))
                {
                    double denom = ic[i - 1] - ic[i];
                    double frac = Math.Abs(denom) < 1e-30 ? 1.0
                        : Math.Min(1.0, Math.Max(0.0, ic[i - 1] / denom));
                    zeroF = (i - 1) + frac;
                    break;
                }
            }
            if (zeroF < 0) { r.Message = "未找到电流过零点"; return r; }

            //Trr：过零 → |Irr|回落到25%（线性插值取小数回落点）
            //2026-10-31 修复：原实现在窗内找不到回落点时静默用窗尾 w1 兜底，Trr/Err/Qrr 被系统性高估。
            //现在找不到就标记这些参数无效，不再给错数。
            double th25 = 0.25 * r.Irr;
            double? endF = null;
            for (int i = peakIdx; i <= w1; i++)
            {
                if (Math.Abs(ic[i]) < th25)
                {
                    double a = Math.Abs(ic[i - 1]), b = Math.Abs(ic[i]);
                    double frac = Math.Abs(a - b) < 1e-30 ? 0.0
                        : Math.Min(1.0, Math.Max(0.0, (a - th25) / (a - b)));
                    endF = (i - 1) + frac;
                    break;
                }
            }

            //di/dt：Irr/(过零→峰值时间)，A/us（与终点无关，始终可算）
            double tRiseUs = (peakIdx - zeroF) * dt / 1e-6;
            if (tRiseUs > 1e-9) r.DiDt = r.Irr / Math.Abs(tRiseUs);
            else r.Invalid.Add("DiDt");

            //Vrr：恢复窗内二极管反向电压峰值（注意符号，取绝对值）
            r.Vrr = 0;
            for (int i = w0; i <= w1; i++) r.Vrr = Math.Max(r.Vrr, Math.Abs(vce[i]));

            double[] p = new double[n]; double[] q = new double[n];
            for (int i = 0; i < n; i++) { p[i] = Math.Abs(vce[i] * ic[i]); q[i] = Math.Abs(ic[i]); }

            if (endF != null)
            {
                r.Trr = (endF.Value - zeroF) * dt * 1e9;
                //Err = ∫Vd·Irr dt 与 Qrr = ∫|Irr| dt（过零到回落点，分数端点）
                r.Err = DptMath.IntegrateFrac(t, p, zeroF, endF.Value) * 1e6;   //uJ
                r.Qrr = DptMath.IntegrateFrac(t, q, zeroF, endF.Value) * 1e6;   //uC（A·s=C→×1e6为uC）
                int zi = (int)Math.Floor(zeroF), ei = (int)Math.Ceiling(endF.Value);
                double pm = 0; for (int i = zi; i <= ei && i < n; i++) pm = Math.Max(pm, Math.Abs(vce[i] * ic[i]));
                r.Pdmax = pm / 1000.0;   //kW
            }
            else
            {
                r.Invalid.Add("Trr"); r.Invalid.Add("Err"); r.Invalid.Add("Qrr"); r.Invalid.Add("Pdmax");
                r.Message = "电流未在恢复窗内回落到25% Irr，Trr/Err/Qrr/Pdmax 已标记无效（可调大后窗）";
            }

            //dv/dt：Vd 10%→90%（恢复沿）
            double vLo = DptMath.Percentile(vce, w0, (int)onIdx.Value, 0.05);
            double vHi = r.Vrr;
            double[] vceS = DptMath.Smooth(vce, dt, 20);
            double? dv = DptMath.SlopeBetween(vceS, dt, (int)Math.Floor(zeroF), w1,
                vLo + 0.1 * (vHi - vLo), vLo + 0.9 * (vHi - vLo), true);
            if (dv != null) r.DvDt = dv.Value / 1e3;   //kV/us
            else r.Invalid.Add("DvDt");

            if (string.IsNullOrEmpty(r.Message))
                r.Message = r.Invalid.Count > 0 ? "提取完成（部分参数无效）" : "提取完成";
            return r;
        }
    }
}
