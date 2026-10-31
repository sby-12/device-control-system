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
        }

        public static Result Analyze(double[] vge, double[] vce, double[] ic, double dt)
        {
            Result r = new Result();
            int n = vge.Length;
            if (n < 100) { r.Message = "数据长度不足"; return r; }
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

            //恢复窗：开通沿前100ns到后500ns
            int w0 = Math.Max(0, (int)onIdx.Value - (int)(0.1e-6 / dt));
            int w1 = Math.Min(n - 1, (int)onIdx.Value + (int)(0.5e-6 / dt));

            //Irr：窗内电流极值（上桥为正峰、下桥可能为负，取绝对值最大）
            int peakIdx = w0; double peakAbs = 0;
            for (int i = w0; i <= w1; i++)
            {
                if (Math.Abs(ic[i]) > peakAbs) { peakAbs = Math.Abs(ic[i]); peakIdx = i; }
            }
            if (peakAbs < 0.1) { r.Message = "恢复电流≈0，检查电流通道"; return r; }
            r.Irr = Math.Abs(ic[peakIdx]);
            bool peakNegative = ic[peakIdx] < 0;

            //过零点：峰之前最后一次穿越0
            int zeroIdx = -1;
            for (int i = peakIdx; i > w0; i--)
            {
                if ((ic[i - 1] > 0 && ic[i] <= 0) || (ic[i - 1] < 0 && ic[i] >= 0)) { zeroIdx = i; break; }
            }
            if (zeroIdx < 0) { r.Message = "未找到电流过零点"; return r; }

            //Trr：过零 → |Irr|回落到25%（沿峰值极性判断方向）
            double th25 = 0.25 * r.Irr;
            int endIdx = w1;
            for (int i = peakIdx; i <= w1; i++)
            {
                if (Math.Abs(ic[i]) < th25) { endIdx = i; break; }
            }
            r.Trr = (endIdx - zeroIdx) * dt * 1e9;

            //di/dt：Irr/(过零→峰值时间)，A/us
            double tRiseUs = (peakIdx - zeroIdx) * dt / 1e-6;
            if (tRiseUs > 1e-9) r.DiDt = r.Irr / Math.Abs(tRiseUs);

            //Vrr：恢复窗内二极管反向电压峰值（注意符号，取绝对值）
            r.Vrr = 0;
            for (int i = w0; i <= w1; i++) r.Vrr = Math.Max(r.Vrr, Math.Abs(vce[i]));

            //Err = ∫Vd·Irr dt 与 Qrr = ∫|Irr| dt（过零到回落点）
            double[] p = new double[n]; double[] q = new double[n];
            for (int i = 0; i < n; i++) { p[i] = Math.Abs(vce[i] * ic[i]); q[i] = Math.Abs(ic[i]); }
            r.Err = DptMath.Integrate(t, p, zeroIdx, endIdx) * 1e6;   //uJ
            r.Qrr = DptMath.Integrate(t, q, zeroIdx, endIdx) * 1e6;   //uC（A·s=C→×1e6为uC）

            double pm = 0; for (int i = zeroIdx; i <= endIdx; i++) pm = Math.Max(pm, Math.Abs(vce[i] * ic[i]));
            r.Pdmax = pm / 1000.0;   //kW

            //dv/dt：Vd 10%→90%（恢复沿）
            double vLo = DptMath.Percentile(vce, w0, (int)onIdx.Value, 0.05);
            double vHi = r.Vrr;
            double[] vceS = DptMath.Smooth(vce, dt, 20);
            double? dv = DptMath.SlopeBetween(vceS, dt, zeroIdx, w1,
                vLo + 0.1 * (vHi - vLo), vLo + 0.9 * (vHi - vLo), true);
            if (dv != null) r.DvDt = dv.Value / 1e3;   //kV/us

            r.Message = "提取完成";
            return r;
        }
    }
}
