using System;

namespace MainSoftwareV100
{
    /// <summary>
    /// 雪崩参数提取（对应UI的"UIS"页签）。
    /// 方法：非钳位感性开关（UIS），器件带电感关断进入雪崩。
    /// EAS=∫Vds·Id dt（雪崩持续期），标准参考 JEDEC 雪崩耐量测试 / IEC 60747-9 SOA。
    /// </summary>
    public static class DptAvalanche
    {
        public class Result
        {
            public double Ias;      //雪崩电流峰值 A
            public double Eas;      //雪崩能量 mJ
            public double Tav;      //雪崩持续时间 us
            public double Vav;      //雪崩电压平台值 V（≈击穿电压）
            public double DiDt;     //关断电流斜率 A/us
            public string Message = "";
        }

        public static Result Analyze(double[] vce, double[] ic, double dt)
        {
            Result r = new Result();
            int n = vce.Length;
            if (n < 100 || ic.Length != n) { r.Message = "数据长度不足"; return r; }
            double[] t = DptMath.TimeAxis(n, dt, 0);
            double[] vceS = DptMath.Smooth(vce, dt, 20);

            //母线电压：波形大部分时间的电压水平
            double vdc = DptMath.Percentile(vceS, 0.5);
            //雪崩判定：Vds超过母线+20%（或绝对+20V）的区间
            double vTh = Math.Max(vdc * 1.2, vdc + 20);
            int a0 = -1, a1 = -1;
            for (int i = 0; i < n; i++)
            {
                if (vceS[i] > vTh) { if (a0 < 0) a0 = i; a1 = i; }
                else if (a0 >= 0 && i > a1 + (int)(0.05e-6 / dt)) break;
            }
            if (a0 < 0 || a1 - a0 < 3) { r.Message = "未检测到雪崩区间（Vds未超过母线+20%）"; return r; }

            //IAS：雪崩窗内电流峰值
            r.Ias = 0;
            for (int i = a0; i <= a1; i++) r.Ias = Math.Max(r.Ias, Math.Abs(ic[i]));

            //EAS：雪崩窗∫V·I dt；tAV：窗时长；VAV：窗内电压95%分位
            double[] p = new double[n];
            for (int i = 0; i < n; i++) p[i] = Math.Abs(vce[i] * ic[i]);
            r.Eas = DptMath.Integrate(t, p, a0, a1) * 1e3;    //J→mJ
            r.Tav = (a1 - a0) * dt / 1e-6;
            r.Vav = DptMath.Percentile(vceS, a0, a1, 0.95);

            //di/dt：雪崩开始前电流下降沿 90%→10%
            int seg0 = Math.Max(0, a0 - (int)(0.5e-6 / dt));
            double icm = 0; for (int i = seg0; i < a0; i++) icm = Math.Max(icm, Math.Abs(ic[i]));
            if (icm > 0.1)
            {
                double[] icS = DptMath.Smooth(ic, dt, 20);
                double? d = DptMath.SlopeBetween(icS, dt, seg0, a0, 0.9 * icm, 0.1 * icm, false);
                if (d != null) r.DiDt = d.Value;
            }

            r.Message = "提取完成";
            return r;
        }
    }
}
