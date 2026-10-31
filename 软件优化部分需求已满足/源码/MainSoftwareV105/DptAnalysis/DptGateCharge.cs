using System;

namespace MainSoftwareV100
{
    /// <summary>
    /// 栅极电荷参数提取（对应UI的"QG-L/QG-R"页签）。
    /// 方法：栅极恒流Ig充电，Qg=∫Ig dt（JEDEC JESD24 栅极电荷测试法）。
    /// 需要两个通道：vge=栅极电压，ig=栅极电流。米勒平台从Vge斜率拐点识别。
    /// </summary>
    public static class DptGateCharge
    {
        public class Result
        {
            public double Qg;        //总栅极电荷 nC
            public double Qgs;       //栅源电荷 nC（0到米勒平台起点）
            public double Qgd;       //米勒（栅漏）电荷 nC（平台起点到终点）
            public double Vgp;       //米勒平台电压 V
            public string Message = "";
        }

        public static Result Analyze(double[] vge, double[] ig, double dt)
        {
            Result r = new Result();
            int n = vge.Length;
            if (n < 100 || ig.Length != n) { r.Message = "数据长度不足"; return r; }
            double[] t = DptMath.TimeAxis(n, dt, 0);
            double[] vgeS = DptMath.Smooth(vge, Math.Max(3, (int)(20e-9 / dt)));

            //总电荷：全程∫Ig dt（假设波形从关断记录到充电完成）
            double qg = 0;
            for (int i = 1; i < n; i++) qg += 0.5 * (Math.Abs(ig[i - 1]) + Math.Abs(ig[i])) * (t[i] - t[i - 1]);
            r.Qg = qg * 1e9;   //C→nC

            //米勒平台识别：Vge上升沿中"斜率明显变缓"的最长水平段
            double vLo = DptMath.Percentile(vgeS, 0.05), vHi = DptMath.Percentile(vgeS, 0.95);
            double span = vHi - vLo;
            if (span < 0.5) { r.Message = "Vge摆幅不足，未找到充电过程"; return r; }

            //Vge最大斜率作基准，找斜率<15%最大斜率且持续最长的平台段
            int step = Math.Max(1, (int)(10e-9 / dt));   //斜率评估步长10ns
            double maxSlope = 0;
            for (int i = step; i < n; i += step)
                maxSlope = Math.Max(maxSlope, Math.Abs(vgeS[i] - vgeS[i - step]) / step);
            if (maxSlope <= 0) { r.Message = "Vge无变化"; return r; }

            double thr = 0.15 * maxSlope;
            int bestLen = 0, bestStart = -1, curStart = -1;
            for (int i = step; i < n; i += step)
            {
                double s = Math.Abs(vgeS[i] - vgeS[i - step]) / step;
                bool plat = s < thr && vgeS[i] > vLo + 0.2 * span && vgeS[i] < vHi - 0.05 * span;
                if (plat && curStart < 0) curStart = i - step;
                if (!plat && curStart >= 0)
                {
                    if (i - curStart > bestLen) { bestLen = i - curStart; bestStart = curStart; }
                    curStart = -1;
                }
            }
            if (curStart >= 0 && n - curStart > bestLen) { bestLen = n - curStart; bestStart = curStart; }

            if (bestStart < 0 || bestLen < step * 2)
            { r.Message = "未识别到米勒平台（平台段太短或斜率太大）"; r.Qgs = 0; r.Qgd = 0; return r; }

            int platEnd = Math.Min(n - 1, bestStart + bestLen);
            r.Vgp = DptMath.Percentile(vgeS, bestStart, platEnd, 0.5);

            //Qgs：0→平台起点；Qgd：平台起点→终点
            double Charge(int a, int b)
            {
                double s = 0;
                for (int i = Math.Max(1, a) + 1; i <= Math.Min(b, n - 1); i++)
                    s += 0.5 * (Math.Abs(ig[i - 1]) + Math.Abs(ig[i])) * (t[i] - t[i - 1]);
                return s * 1e9;
            }
            r.Qgs = Charge(0, bestStart);
            r.Qgd = Charge(bestStart, platEnd);
            r.Message = "提取完成";
            return r;
        }
    }
}
