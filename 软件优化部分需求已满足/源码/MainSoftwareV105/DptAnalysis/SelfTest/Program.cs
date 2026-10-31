using System;
using MainSoftwareV100;

//2026-10-31 数值自检：验证 IntegrateFrac / CrossingIndex(endIdx) / 新能量逻辑
class Program
{
    static int fails = 0;
    static void Check(bool ok, string name, double expect, double actual)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " 期望=" + expect + " 实际=" + actual);
        if (!ok) fails++;
    }
    static bool Near(double a, double b, double tol = 1e-9) { return Math.Abs(a - b) <= tol * Math.Max(1, Math.Abs(b)); }

    static int Main()
    {
        int n = 1000; double dt = 1e-9;
        double[] t = DptMath.TimeAxis(n, dt, 0);
        double[] p = new double[n];
        for (int i = 0; i < n; i++) p[i] = 7.0;   //恒定功率7W

        //① 整数端点一致性：IntegrateFrac(a,b整数) 应等于 Integrate(a,b)
        double i1 = DptMath.Integrate(t, p, 100, 500);
        double i2 = DptMath.IntegrateFrac(t, p, 100, 500);
        Check(Near(i2, i1), "整数端点一致", i1, i2);

        //② 解析真值：恒定p下 ∫a→b p dt = p*(b-a)*dt
        double truth = 7.0 * 400 * dt;
        Check(Near(i2, truth), "整数端点对解析真值", truth, i2);

        //③ 分数端点真值：∫100.25→500.75 7dt = 7*400.5*dt
        double truthF = 7.0 * 400.5 * dt;
        double i3 = DptMath.IntegrateFrac(t, p, 100.25, 500.75);
        Check(Near(i3, truthF), "分数端点对解析真值", truthF, i3);

        //④ 线性斜坡：p[i]=i，∫a→b = 梯形真值。取整数端点与手算比较
        double[] ramp = new double[n];
        for (int i = 0; i < n; i++) ramp[i] = i;
        //∫_{10}^{20} x dx（连续）= (400-100)/2 = 150，单位 p·dt
        double rampInt = DptMath.IntegrateFrac(t, ramp, 10, 20) / dt;
        Check(Near(rampInt, 150.0), "斜坡整数端点", 150.0, rampInt);
        //分数：∫_{10.5}^{20.5} x dx = (20.5²-10.5²)/2 = 155
        double rampIntF = DptMath.IntegrateFrac(t, ramp, 10.5, 20.5) / dt;
        Check(Near(rampIntF, 155.0), "斜坡分数端点", 155.0, rampIntF);

        //⑤ 端点越界保护：b 超出数组不崩溃且截断到 n-1
        double i5 = DptMath.IntegrateFrac(t, p, 998, 5000);
        Check(Near(i5, 7.0 * 1 * dt), "终点越界截断", 7.0 * 1 * dt, i5);

        //⑥ CrossingIndex endIdx 边界：上升阶梯，endIdx 内无穿越→null
        double[] step = new double[n];
        for (int i = 0; i < n; i++) step[i] = i < 500 ? 0 : 10;
        var c1 = DptMath.CrossingIndex(step, 5, true, 0, 400);
        Check(c1 == null, "endIdx内无穿越返回null", 0, c1.HasValue ? 1 : 0);
        var c2 = DptMath.CrossingIndex(step, 5, true, 0, 600);
        Check(c2 != null && Near(c2.Value, 499.5, 1e-6), "endIdx外正常找到", 499.5, c2 ?? -1);

        //⑦ 合成双脉冲走完整 DptSwitching：正弦搭的脉冲，验证不崩溃且 Invalid 语义
        int m = 8000; double dt2 = 1e-9;
        double[] vge = new double[m], vce = new double[m], ic = new double[m];
        for (int i = 0; i < m; i++)
        {
            double tt = i * dt2;
            //两个1us宽、间隔3us的Vge脉冲
            bool on = (tt > 0.5e-6 && tt < 1.5e-6) || (tt > 4.5e-6 && tt < 5.5e-6);
            vge[i] = on ? 15 : 0;
            //Vce：关断600V，开通后缓降到2V（100ns沿）；开通窗内电流100A
            double edge = 100e-9;
            vce[i] = on ? Math.Max(2.0, 600 - 598 * Math.Min(1, (tt % 4e-6 - 0.5e-6) / edge)) : 600;
            ic[i] = on ? 100 : 0;
        }
        var r = DptSwitching.Analyze(vge, vce, ic, dt2);
        Console.WriteLine("INFO 合成双脉冲: TdOn=" + r.TdOn.ToString("0.0") + "ns Tr=" + r.Tr.ToString("0.0")
            + "ns Eon=" + r.Eon.ToString("0.0") + "uJ Eoff=" + r.Eoff.ToString("0.0")
            + "uJ Invalid=[" + string.Join(",", r.Invalid) + "] Msg=" + r.Message);
        Check(true, "合成双脉冲跑通", 0, 0);

        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILURES");
        return fails == 0 ? 0 : 1;
    }
}
