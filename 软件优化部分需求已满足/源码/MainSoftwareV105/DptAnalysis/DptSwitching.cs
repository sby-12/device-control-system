using System;

namespace MainSoftwareV100
{
    /// <summary>
    /// 开关参数提取（对应UI的"开关-L/开关-R"页签）。
    /// 定义来源：IEC 60747-9（IGBT开关时序与能量），MOSFET对应JEDEC JESD24。
    /// C#移植自DPT开源项目 detect/pulse_detector.py + metrics/iec_timings.py + metrics/slopes.py + metrics/energy.py 的核心路径，
    /// 边缘工况容错做了简化——异常时对应参数置0并在Message里说明，不静默给错数。
    /// </summary>
    public static class DptSwitching
    {
        /// <summary>一次开关参数提取的完整结果。所有时间ns、能量uJ、Ls为nH、Pmax为kW。</summary>
        public class Result
        {
            public double TdOn, Tr, Ton;          //开通延迟/上升时间/总开通时间 (ns)
            public double TdOff, Tf, Toff;        //关断延迟/下降时间/总关断时间 (ns)
            public double Tz;                     //电流拖尾时间 10%→2% Ic (ns)，IGBT特性，MOSFET无拖尾为0
            public double DvDtOn, DvDtOff;        //kV/us
            public double DiDtOn, DiDtOff;        //A/us
            public double Eon, Eoff;              //uJ
            public double Vdsm;                   //关断过冲峰值 V
            public double DeltaVce;               //过冲超出母线的部分 V
            public double Ls;                     //换流回路杂散电感估算 nH，Ls=ΔVce/(di/dt_off)
            public double Pmax;                   //开关窗内 V×I 峰值 kW
            public double CrosstalkVmax;          //对管Vge正向尖峰 V（无对管通道时为0）
            public double CrosstalkVmin;          //对管Vge负向尖峰 V
            public string Message = "";           //提取过程说明/异常提示
            //2026-10-31 逐参数有效状态：含参数名表示该参数不可信/未求得，界面应显示"--"而不是0
            public readonly System.Collections.Generic.HashSet<string> Invalid =
                new System.Collections.Generic.HashSet<string>();
        }

        //内部：Vge脉冲沿位置
        private class PulseEdges
        {
            public int P1On = -1, P1Off = -1, P2On = -1, P2Off = -1;
        }

        /// <summary>
        /// 主入口。vge/vce/ic为三个通道的等间隔采样，dt为采样间隔（秒），other为对管Vge（可空，串扰用）。
        /// </summary>
        public static Result Analyze(double[] vge, double[] vce, double[] ic, double dt, double[] other = null)
        {
            Result r = new Result();
            int n = vge == null ? 0 : vge.Length;
            if (n < 100 || vce == null || ic == null || vce.Length != n || ic.Length != n)
            { r.Message = "数据长度不足或三通道长度不一致"; return r; }
            if (!(dt > 0) || double.IsNaN(dt) || double.IsInfinity(dt))
            { r.Message = "采样间隔无效（dt≤0/NaN/∞），拒绝提取，请检查波形来源"; return r; }

            double[] t = DptMath.TimeAxis(n, dt, 0);

            //========== ① 门极脉冲检测（DPT的迟滞比较法简化版）==========
            PulseEdges e = DetectPulses(vge, dt);
            if (e.P1Off < 0 || e.P2On < 0)
            { r.Message = "未识别到双脉冲门极波形（需要至少两个Vge脉冲）"; return r; }

            //========== ② 切窗（DPT segmenter的比例窗简化版）==========
            int p1w = Math.Max(10, e.P1Off - e.P1On);
            int gap = Math.Max(10, e.P2On - e.P1Off);
            int preOff = Math.Min((int)(3.0e-6 / dt), Math.Max((int)(0.05 * p1w), 10));
            int postOff = Math.Min((int)(1.5e-6 / dt), Math.Max((int)(0.1 * p1w), 10));
            int preOn = Math.Min((int)(1.5e-6 / dt), Math.Max((int)(0.05 * gap), 10));
            int postOn = Math.Min((int)(2.5e-6 / dt), Math.Max((int)(0.2 * gap), 10));
            int off0 = Math.Max(0, e.P1Off - preOff), off1 = Math.Min(n - 1, e.P1Off + postOff);
            int on0 = Math.Max(0, e.P2On - preOn), on1 = Math.Min(n - 1, e.P2On + postOn);

            //========== ③ 平台电平（分位数估计，避开开关沿）==========
            //Icm：第一脉冲导通平台 |Ic| 95%分位
            int plat1 = Math.Max(e.P1On + 10, Math.Min(e.P1Off - (int)(0.04 * p1w), n - 1));
            int plat0 = Math.Max(e.P1On, plat1 - (int)(0.25 * p1w));
            double icm = DptMath.Percentile(Abs(ic), plat0, plat1, 0.95);
            if (icm < 1e-9) { r.Message = "导通平台电流≈0，检查Ic通道映射"; return r; }

            //Vge高/低电平：高电平取第一脉冲平台，低电平取关断后平台
            double vgeHi = DptMath.Percentile(vge, e.P1On, Math.Max(e.P1On + 5, e.P1Off - preOff / 2), 0.95);
            double vgeLo = DptMath.Percentile(vge, Math.Min(n - 1, e.P1Off + (int)(0.15e-6 / dt)), Math.Min(n - 1, e.P1Off + (int)(1.0e-6 / dt)), 0.05);
            //Vce高/低电平：高电平取关断后平台，低电平取导通平台
            double vceHi = DptMath.Percentile(vce, Math.Min(n - 1, e.P1Off + (int)(0.15e-6 / dt)), Math.Min(n - 1, e.P1Off + (int)(1.0e-6 / dt)), 0.95);
            double vceLo = DptMath.Percentile(vce, plat0, plat1, 0.05);

            double v10 = DptMath.ThresholdValue(vgeLo, vgeHi, 0.10);
            double v90 = DptMath.ThresholdValue(vgeLo, vgeHi, 0.90);
            double i10 = 0.10 * icm, i90 = 0.90 * icm, i02 = 0.02 * icm;
            double vceSpan = vceHi - vceLo;
            if (vceSpan < 1e-6) vceSpan = Math.Max(Math.Abs(vceHi), 1.0);   //退化成按绝对值

            double[] vgeS = DptMath.Smooth(vge, dt, 20);    //20ns检测平滑
            double[] icS = DptMath.Smooth(Abs(ic), dt, 20);
            double[] vceS = DptMath.Smooth(vce, dt, 20);

            //========== ④ 开通时间参数（IEC 60747-9）==========
            double? tv10 = DptMath.CrossingIndex(vgeS, v10, true, on0);              //Vge升10%
            double? ti10on = tv10 != null ? DptMath.CrossingIndex(icS, i10, true, (int)tv10.Value + 1) : null;
            double? ti90on = ti10on != null ? DptMath.CrossingIndex(icS, i90, true, (int)Math.Ceiling(ti10on.Value) + 1) : null;
            if (tv10 != null && ti10on != null)
            {
                r.TdOn = Math.Max(0, (ti10on.Value - tv10.Value)) * dt * 1e9;
                if (ti90on != null) r.Tr = (ti90on.Value - ti10on.Value) * dt * 1e9;
                else { r.Invalid.Add("Tr"); r.Invalid.Add("Ton"); }
                r.Ton = r.TdOn + r.Tr;
            }
            else { r.Invalid.Add("TdOn"); r.Invalid.Add("Tr"); r.Invalid.Add("Ton"); }

            //========== ⑤ 关断时间参数 + Tz 拖尾（IEC 60747-9）==========
            double? tv90 = DptMath.CrossingIndex(vgeS, v90, false, off0);            //Vge降90%
            double? ti90off = tv90 != null ? DptMath.CrossingIndex(icS, i90, false, (int)tv90.Value + 1) : null;
            double? ti10off = ti90off != null ? DptMath.CrossingIndex(icS, i10, false, (int)Math.Ceiling(ti90off.Value) + 1) : null;
            if (tv90 != null && ti90off != null)
            {
                r.TdOff = Math.Max(0, (ti90off.Value - tv90.Value)) * dt * 1e9;
                if (ti10off != null)
                {
                    r.Tf = (ti10off.Value - ti90off.Value) * dt * 1e9;
                    //Tz：电流拖尾时间，10%→2%（IGBT少子复合拖尾；MOSFET无拖尾→找不到穿越即0）
                    double? ti02 = DptMath.CrossingIndex(icS, i02, false, (int)Math.Ceiling(ti10off.Value) + 1);
                    if (ti02 != null) r.Tz = (ti02.Value - ti10off.Value) * dt * 1e9;
                }
                else { r.Invalid.Add("Tf"); r.Invalid.Add("Toff"); }
                r.Toff = r.TdOff + r.Tf;
            }
            else { r.Invalid.Add("TdOff"); r.Invalid.Add("Tf"); r.Invalid.Add("Toff"); }

            //========== ⑥ 斜率（10%~90%，kV/us 与 A/us）==========
            double? dvon = DptMath.SlopeBetween(vceS, dt, on0, on1,
                DptMath.ThresholdValue(vceLo, vceHi, 0.90), DptMath.ThresholdValue(vceLo, vceHi, 0.10), false);
            if (dvon != null) r.DvDtOn = dvon.Value / 1e3;          //V/us → kV/us
            else r.Invalid.Add("DvDtOn");
            double? dvoff = DptMath.SlopeBetween(vceS, dt, off0, off1,
                DptMath.ThresholdValue(vceLo, vceHi, 0.10), DptMath.ThresholdValue(vceLo, vceHi, 0.90), true);
            if (dvoff != null) r.DvDtOff = dvoff.Value / 1e3;
            else r.Invalid.Add("DvDtOff");
            double? dion = DptMath.SlopeBetween(icS, dt, on0, on1, i10, i90, true);
            if (dion != null) r.DiDtOn = dion.Value;
            else r.Invalid.Add("DiDtOn");
            double? dioff = DptMath.SlopeBetween(icS, dt, off0, off1, i90, i10, false);
            if (dioff != null) r.DiDtOff = dioff.Value;
            else r.Invalid.Add("DiDtOff");

            //========== ⑦ 能量（IEC 60747-9：∫Vce·Ic dt，分数端点梯形法）==========
            //2026-10-31 修复：原实现把已插值的穿越点强转(int)再积到整数端点，且找不到终点
            //时静默积到窗尾，能量被系统性高估。现改用 IntegrateFrac 按插值点精确积分，
            //终点在窗内找不到时把该能量标记为无效（Invalid），不再给错数。
            double[] p = new double[n];
            for (int i = 0; i < n; i++) p[i] = vce[i] * ic[i];
            //Eon：Vge升10%（插值点）→ Vce降到 Lo+2%×span（插值点），搜索限在开通窗内
            if (tv10 != null)
            {
                double vEnd = vceLo + 0.02 * vceSpan;
                double? endOn = DptMath.CrossingIndex(vceS, vEnd, false, (int)Math.Ceiling(tv10.Value) + 1, on1);
                if (endOn != null && endOn.Value > tv10.Value)
                    r.Eon = DptMath.IntegrateFrac(t, p, tv10.Value, endOn.Value) * 1e6;    //J→uJ
                else
                    r.Invalid.Add("Eon");
            }
            else r.Invalid.Add("Eon");
            //Eoff：Vge降90%（插值点）→ Ic降到2% Icm（插值点），搜索限在关断窗内
            if (tv90 != null)
            {
                double? endOff = DptMath.CrossingIndex(icS, i02, false, (int)Math.Ceiling(tv90.Value) + 1, off1);
                if (endOff != null && endOff.Value > tv90.Value)
                    r.Eoff = DptMath.IntegrateFrac(t, p, tv90.Value, endOff.Value) * 1e6;
                else
                    r.Invalid.Add("Eoff");
            }
            else r.Invalid.Add("Eoff");

            //========== ⑧ 过冲/杂散电感/峰值功率（DPT口径）==========
            r.Vdsm = MaxAbs(vce, off0, off1);
            r.DeltaVce = Math.Max(0, r.Vdsm - vceHi);
            if (r.DiDtOff > 0) r.Ls = r.DeltaVce / r.DiDtOff * 1000;   //nH = V/(A/us)×1000
            else r.Invalid.Add("Ls");   //Ls依赖di/dt_off，斜率无效则杂散电感也不可信
            double pmaxW = 0;
            for (int i = off0; i <= off1; i++) pmaxW = Math.Max(pmaxW, Math.Abs(p[i]));
            for (int i = on0; i <= on1; i++) pmaxW = Math.Max(pmaxW, Math.Abs(p[i]));
            r.Pmax = pmaxW / 1000.0;   //kW

            //串扰：对管Vge在开关窗内的正负尖峰
            if (other != null && other.Length == n)
            {
                double mx = double.MinValue, mn = double.MaxValue;
                for (int i = off0; i <= off1; i++) { mx = Math.Max(mx, other[i]); mn = Math.Min(mn, other[i]); }
                for (int i = on0; i <= on1; i++) { mx = Math.Max(mx, other[i]); mn = Math.Min(mn, other[i]); }
                r.CrosstalkVmax = mx; r.CrosstalkVmin = mn;
            }

            r.Message = "提取完成（" + (tv10 != null && tv90 != null ? "双脉冲" : "部分沿缺失") + "）";
            if (r.Invalid.Count > 0)
                r.Message += "；" + string.Join("/", r.Invalid) + " 终点未找到，已标记无效";
            return r;
        }

        //========== 内部工具 ==========

        /// <summary>门极脉冲检测：分位数定电平 + 50%/25%迟滞，找脉冲1关断沿与脉冲2开通沿。</summary>
        private static PulseEdges DetectPulses(double[] vge, double dt)
        {
            PulseEdges e = new PulseEdges();
            int n = vge.Length;
            double[] vs = DptMath.Smooth(vge, Math.Max(3, (int)(50e-9 / dt)));
            double lo = DptMath.Percentile(vs, 0.02), hi = DptMath.Percentile(vs, 0.98);
            double span = hi - lo;
            if (span < 1e-6) return e;
            double thOn = lo + 0.5 * span, thOff = lo + 0.25 * span;

            //迟滞状态机提取所有开通区间
            bool state = vs[0] > thOn;
            int riseStart = state ? 0 : -1;
            int minWidth = Math.Max(5, (int)(0.3e-6 / dt));
            var pulses = new System.Collections.Generic.List<int[]>();   //[on,off]
            for (int i = 1; i < n; i++)
            {
                if (state && vs[i] < thOff)
                {
                    if (i - riseStart >= minWidth) pulses.Add(new int[] { riseStart, i });
                    state = false;
                }
                else if (!state && vs[i] > thOn)
                {
                    riseStart = i; state = true;
                }
            }
            if (pulses.Count < 2) return e;
            e.P1On = pulses[0][0]; e.P1Off = pulses[0][1];
            e.P2On = pulses[1][0]; e.P2Off = pulses[1][1];
            return e;
        }

        private static double[] Abs(double[] y)
        {
            double[] r = new double[y.Length];
            for (int i = 0; i < y.Length; i++) r[i] = Math.Abs(y[i]);
            return r;
        }

        private static double MaxAbs(double[] y, int i0, int i1)
        {
            i0 = Math.Max(0, i0); i1 = Math.Min(y.Length - 1, i1);
            double m = 0;
            for (int i = i0; i <= i1; i++) m = Math.Max(m, Math.Abs(y[i]));
            return m;
        }
    }
}
