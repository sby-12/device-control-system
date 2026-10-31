using System;

namespace MainSoftwareV100
{
    /// <summary>
    /// 短路参数提取（对应UI的"SC"页签）。
    /// 定义来源：IEC 60747-9 短路耐量型式试验（Tsc=短路耐量时间）。
    /// Esc=∫Vce·Ic dt 短路期间能量，Desat时间=从短路发生到退饱和（Vce回落）的时间。
    /// 注意：短路测试有炸管风险，此模块只做事后波形分析。
    /// </summary>
    public static class DptShortCircuit
    {
        public class Result
        {
            public double Imax;     //短路电流峰值 A
            public double Tsc;      //短路时间 us（电流>10% Imax的持续期）
            public double Esc;      //短路能量 mJ
            public double Vpeak;    //短路期间Vce峰值 V
            public double DesatTime;//退饱和时间 us（Imax出现→Vce跌破90%峰值）
            public string Message = "";
        }

        public static Result Analyze(double[] vce, double[] ic, double dt)
        {
            Result r = new Result();
            int n = vce.Length;
            if (n < 100 || ic.Length != n) { r.Message = "数据长度不足"; return r; }
            double[] t = DptMath.TimeAxis(n, dt, 0);
            double[] icS = DptMath.Smooth(ic, dt, 20);

            //Imax与峰值位置
            int im = 0; r.Imax = 0;
            for (int i = 0; i < n; i++) if (Math.Abs(icS[i]) > r.Imax) { r.Imax = Math.Abs(icS[i]); im = i; }
            if (r.Imax < 0.1) { r.Message = "电流≈0，无短路事件"; return r; }

            //短路区间：电流从10% Imax上升到回落到10% Imax
            double th = 0.1 * r.Imax;
            int? s0 = (int?)DptMath.CrossingIndex(icS, th, true, 1);
            int? s1 = (int?)DptMath.CrossingIndex(icS, th, false, im);
            if (s0 == null) { r.Message = "未识别到短路电流上升沿"; return r; }
            int end = s1 != null ? (int)s1.Value : n - 1;

            r.Tsc = (end - s0.Value) * dt / 1e-6;

            //Esc：短路区间∫Vce·Ic dt；Vpeak：窗内Vce峰值
            double[] p = new double[n];
            for (int i = 0; i < n; i++) p[i] = Math.Abs(vce[i] * ic[i]);
            r.Esc = DptMath.Integrate(t, p, (int)s0.Value, end) * 1e3;   //mJ
            r.Vpeak = 0;
            for (int i = (int)s0.Value; i <= end; i++) r.Vpeak = Math.Max(r.Vpeak, Math.Abs(vce[i]));

            //退饱和时间：峰值时刻→Vce跌破90%峰值（Vce被电流拖起后回落即退饱和标志）
            double vTh = 0.9 * r.Vpeak;
            int desat = end;
            for (int i = im; i <= end; i++) { if (Math.Abs(vce[i]) < vTh) { desat = i; break; } }
            r.DesatTime = (desat - im) * dt / 1e-6;

            r.Message = "提取完成";
            return r;
        }
    }
}
