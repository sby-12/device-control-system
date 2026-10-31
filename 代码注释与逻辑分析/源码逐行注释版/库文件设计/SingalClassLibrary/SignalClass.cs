using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
// ⚠ 上面几行 using 里 Runtime.Remoting、Xml.Linq、BitVector32 都没实际用到，
//    是 VS 自动添加或复制粘贴的遗留（属于编译警告的来源，可清理）

namespace SingalClassLibrary
{
    // ⚠ 注意：命名空间和库名拼写是 "Singal"（Signal 的笔误），全项目一致沿用了这个拼写。
    //    改名要四处一起改，动不如不动。
    /// <summary>
    /// 信号源驱动类（SCPI 文本协议，普源 DG 系列风格）。
    /// 工作模式：把波形数据（正弦/方波/自定义脉冲）打包成 ASCII 指令下载到仪器的
    ///           volatile 存储区，再用触发指令输出。
    /// 与示波器库不同：这个库"只发不收"（仪器基本不回数据），见 TcpTranslateClass。
    /// </summary>
    public class SignalClass
    {
        public TcpTranslateClass translateClass;
        public bool OpenData = false;      // ⚠ 只在被注释的代码里出现过，实际未使用
        private int LenData = 800;         // 自定义脉冲波形的点数上限（双脉冲超出时会自动扩，见下）

        public SignalClass()
        {
            translateClass = new TcpTranslateClass();
        }

        //通信连接
        public void OpenTranslate(string host, int port)
        {
            _ = translateClass.ConnectAsync(host, port);
           
        }

        //通信关闭
        public void CloseTranslate()
        {
            translateClass.CloseAsync();
        }


        // ===== 波形生成工具：返回 0~1（或 -1~0.9）的归一化数组，下载时由仪器换算成电压 =====

        /// <summary>
        /// 1. 正弦波：一个完整周期，(sin+1)/2 映射到 0~1
        /// </summary>
        public  double[] Sine(int points)
        {
            double[] data = new double[points];
            for (int i = 0; i < points; i++)
            {
                data[i] = (Math.Sin(2 * Math.PI * i / points) + 1) / 2;
             //   data[i] = (ushort)(v * 65535);   // 16位DAC版本的残影（已注释）
            }
            return data;
        }

        /// <summary>
        /// 2. 三角波：0→1→0
        /// </summary>
        public  double[] Triangle(int points)
        {
            double[] data = new double[points];
            for (int i = 0; i < points; i++)
            {
                data[i] = 1 - Math.Abs(2 * (double)i / points - 1);
            }
            return data;
        }

        /// <summary>
        /// 3. 锯齿波：0 线性升到 ~1
        /// </summary>
        public  double[] Sawtooth(int points)
        {
            double[] data = new double[points];
            for (int i = 0; i < points; i++)
            {
                data[i] = (double)i / points;
               // data[i] = (ushort)(v * 65535);
            }
            return data;
        }

        /// <summary>
        /// 4. 方波（可设置占空比 duty=高电平百分比）。
        /// ⚠ 注意：高电平 0.9、低电平 -1，不是对称的 ±1——这是实验调出来的，让输出电压落在想要区间。
        /// </summary>
        public  double[] Square(int points, double duty = 50)
        {
            double[] data = new double[points];
            double th = points * duty / 100.0;
            for (int i = 0; i < points; i++)
            {
                data[i] = i < th ? (double)0.9 : (double)-1;
            }
            return data;
        }

        /// <summary>
        /// 5. PWM波（16位DAC版本：高电平 65535）。⚠ 主程序从未调用，预留。
        /// </summary>
        public  ushort[] PWM(int points, int pulseWidthPoints = 10)
        {
            ushort[] data = new ushort[points];
            for (int i = 0; i < points; i++)
            {
                data[i] = i < pulseWidthPoints ? (ushort)65535 : (ushort)0;
            }
            return data;
        }

        /// <summary>
        /// 信号源初始化（连接成功后由连接弹窗调用）。
        /// 有效指令：复位 → ARB 任意波模式 → 电压10V/偏移5V（即 0~10V 单极性输出）→
        ///           高阻负载 → 周期 1000us → 脉冲串触发模式、手动触发、1个周期。
        /// 上面大段注释掉的是学长调试过程中的各种方案存档（自动/手动触发、外部触发等）。
        /// ⚠ 潜在问题：固定 Sleep(20) 节流是经验值；且 *RST 后仪器要稳定更久，20ms 可能不够。
        /// </summary>
        public void Init()
        {
            //（中间约 30 行注释掉的方案存档略——原文件保留）

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes("*RST" + "\n"));   // 仪器复位
            Thread.Sleep(200);
            // SystemPlauseInit();
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC ARB" + "\n"));   // 任意波模式
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT 10" + "\n"));     // 幅值 10V
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS 5" + "\n")); // 偏移 5V → 0~10V
            Thread.Sleep(20);

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1:IMP INF" + "\n"));     // 输出负载设为高阻

            //Thread.Sleep(20);
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes("SOUR1:FUNC:ARB 1000000" + "\n"));//:SOUR1:APPL:ARB 500

            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PER 1000us"));
            // ⚠ 潜在问题：这条指令末尾漏了 "\n"，靠下一条指令的换行"蹭"了一个结束符——能工作但脆弱
            Thread.Sleep(20);
            // Thread.Sleep(20);
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:ARB:MODE FREQ" + "\n"));
            //Thread.Sleep(20);
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FREQ 1000000" + "\n"));
            //Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:MODE TRIG" + "\n"));     // 脉冲串：触发模式
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SOUR MAN" + "\n")); // 触发源 = 手动
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS ON" + "\n")); // 打开脉冲串
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:NCYC 1" + "\n"));  // 每次触发输出1个周期
            Thread.Sleep(20);
            
        }

        /// <summary>
        /// 信号发生器赋值设置：高电平设 V，偏移设 V/2（保持单极性 0~V）。
        /// ⚠ 潜在问题：参数是 byte 类型！DianYaValue.Value 转 byte，
        ///    输入超过 255 会溢出，超过 10 的电压值也会直接发给仪器（无范围校验）。
        /// </summary>
        public void ValueSet(byte Value)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT " + Value.ToString()+ "\n"));    
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS " + (Value/2).ToString()+"\n")); 
            // ⚠ Value/2 是整数除法：byte 除法直接取整，5/2=2，电压和偏移会失配
            Thread.Sleep(20);
        }

        /// <summary>
        /// 开关输出（OFF→ON→ON 的抖动是刻意为之：重启输出确保从干净状态开始）。
        /// ⚠ 注意：方法是 private 的，只有本类内部调用（SystemOpen 在脉冲输出方法里被调）。
        /// </summary>
        void SystemOpen()
        {
            //if(!OpenData)
            //{
            //    OpenData = true;
                
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
            Thread.Sleep(100);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 OFF" + "\n"));
            Thread.Sleep(100);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
            Thread.Sleep(100);
            //}
        }

        void SystemOff()
        {
            //if(!OpenData)
            //{
            //    OpenData = true;

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 OFF" + "\n"));
            Thread.Sleep(20);
            //}
        }

        /// <summary>
        /// 备用初始化方案：脉冲模式 + 特定脉宽/周期（当前未被调用，是方案存档）。
        /// </summary>
        void SystemPlauseInit()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC PULSe" + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1:IMP INF" + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:WIDT 0.00001" + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:PULS:PER 0.0001" + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT 5.0" + "\n"));     // 5Vpp
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS 0" + "\n")); // 0V偏移
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:MODE TRIG" + "\n"));     // 5Vpp
            Thread.Sleep(20);

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SOUR MAN" + "\n")); // 触发源 = 手动
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS ON" + "\n")); // 触发源 = 手动
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:NCYC 1" + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
            Thread.Sleep(200);
        }

        /// <summary>
        /// 下载波形到仪器（浮点版本）：:SOUR1:TRAC:DATA:DATA VOLATILE, 逗号分隔的数值串。
        /// 一条指令可能上万个字符——TCP 单包放得下，但仪器解析需要时间，之后要 Sleep。
        /// </summary>
        public void DownloadWave(int ch, double[] data)
        {
           // byte[] binaryData = ConvertDac16ToBinaryBlock(data);
            string cmd = $":SOUR{ch}:TRAC:DATA:DATA  VOLATILE," + string.Join(",", data);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(cmd+"\n"));
        }

        /// <summary>
        /// 将 DAC16 数组转换为仪器要求的二进制块格式（#头+小端数据）。
        /// ⚠ 当前未被调用（浮点 ASCII 方案取代了二进制方案），是保留的工具。
        /// </summary>
        static byte[] ConvertDac16ToBinaryBlock(ushort[] dacData)
        {
            // 每个DAC16占2字节，小端序（DG1000Z 要求）
            byte[] dataBytes = dacData.SelectMany(x => BitConverter.GetBytes(x)).ToArray();
            int dataLen = dataBytes.Length;

            // 二进制块头：# + 长度位数 + 长度值
            string lenStr = dataLen.ToString();
            byte[] header = System.Text.Encoding.ASCII.GetBytes($"#{lenStr.Length}{lenStr}");

            // 合并：头部 + 数据
            return header.Concat(dataBytes).ToArray();
        }

        /// <summary>
        /// 触发一次输出（手动触发源的"按下扳机"）。
        /// </summary>
        public void Apply()
        {
            
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));// 触发源 = 手动
            
        }

        /// <summary>
        /// 脉冲模式下单脉冲宽度设置（备用方案，当前主程序走自定义波形方案）。
        /// </summary>
        public void SinglePlaseSet(double widtime1)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:WIDT " + widtime1.ToString("0.000000") + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));

        }

        /// <summary>
        /// 双脉冲设置（脉冲模式方案，当前未被主程序调用——主程序用 OutputTwoDifferentPulses）。
        /// </summary>
        public void DoublePlaseSet(double widtime1, double widtime2)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes("*RST " + "\n"));
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC PULS" + "\n"));
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:HOLD WIDT" + "\n"));
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:WIDT " + widtime1.ToString("0.000000") + "\n"));
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:PULS:PER " + widtime2.ToString("0.000000") + "\n"));
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT 5.0" + "\n"));     // 5Vpp
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS 0" + "\n")); // 0V偏移

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:MODE TRIG" + "\n"));     // 5Vpp
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:NCYC 2" + "\n")); 
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SOUR MAN" + "\n")); // 触发源 = 手动
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(" *TRG" + "\n"));//:SOUR1:BURS:TRIG
        }

        /// <summary>
        /// 【测试代码】内部硬编码调用双脉冲(200,500,300)（主程序未调用）。
        /// </summary>
        public void SinglePlaseSetTest(double num)
        {
            OutputTwoDifferentPulses(200, 500, 300);
        }

        /// <summary>
        /// ★ 单脉冲输出（主程序"脉冲设置"单选时调用）★
        /// 思路：把"高电平点数 pulse1Width、其余低电平"的 800 点数组下载到仪器 → 开脉冲串 → 触发。
        /// 16383 = 16位DAC一半左右的值（配合 10V 幅值/5V 偏移，输出约 5V 高电平）。
        /// </summary>
        public void OutputOneDifferentPulses(int pulse1Width)
        {

            try
            {
                // 1. 计算总波形长度
                //SystemOff();
                // 2. 生成自定义双脉冲波形（两个不同宽度）
                UInt16[] wave1 = new UInt16[LenData];
                for (int i = 0; i < LenData; i++)
                {
                    if (i < pulse1Width)
                        wave1[i] = 16383; // 第一个脉冲高电平
                    else
                        wave1[i] = 0;
                   
                }

                // 3. 下载波形到仪器

                string cmd = ":DATA:DAC VOLATILE,0," + string.Join(",", wave1);


                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(cmd + "\n"));
                Thread.Sleep(20);
                // 4. 输出配置（只发一次，不循环）
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS ON" + "\n")); // 触发源 = 手动
                Thread.Sleep(20);

                //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
                //Thread.Sleep(20);
                SystemOpen();
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));
                Thread.Sleep(20);
            }
            catch { }
            // ⚠ 潜在问题：catch 吞掉所有异常且不写日志，下载失败时界面毫无感知。
            //    另外 :DATA:DAC 下载大数组后只 Sleep 20ms，仪器可能还没存完就触发，偶发输出旧波形。
        }

        /// <summary>
        /// ★ 双脉冲输出（主程序"脉冲设置"双选时调用）★
        /// 波形布局：[脉宽1高电平][间隔低电平][脉宽2高电平][剩余低电平]。
        /// ⚠ 潜在问题：total > LenData(800) 时把 LenData 永久改大（字段被污染），
        ///    之后再调单脉冲，数组也会按新长度生成——属于"一次双脉冲，全局长度变"的隐性耦合。
        /// </summary>
        public void OutputTwoDifferentPulses(
           int pulse1Width,
           int pulse2Width,
           int interval)
        {
           
            try
            {
                //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC PULSe" + "\n"));
               // SystemOff();
                // 1. 计算总波形长度
                int total = pulse1Width + interval + pulse2Width;
                if(total > LenData)
                {
                    LenData = total;   // ⚠ 见方法注释：永久扩大全局长度
                }
                
                // 2. 生成自定义双脉冲波形（两个不同宽度）
                UInt16[] wave = new UInt16[LenData];
                for (int i = 0; i < LenData; i++)
                {
                    if (i < pulse1Width)
                        wave[i] = 16383; // 第一个脉冲高电平
                    else if (i < pulse1Width + interval)
                        wave[i] = 0;     // 间隔低电平
                    else if(i<= total)
                        wave[i] = 16383; // 第二个脉冲（宽度不同）
                    else
                        wave[i] = 0;
                }

                // 3. 下载波形到仪器
               
                string cmd = ":DATA:DAC VOLATILE,0," + string.Join(",", wave);
                
               
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(cmd + "\n"));
                Thread.Sleep(20);
                // 4. 输出配置（只发一次，不循环）
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS ON" + "\n")); // 触发源 = 手动
                Thread.Sleep(20);
                SystemOpen();
                //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
                //Thread.Sleep(20);
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));
                Thread.Sleep(20);
            }
            catch { }
            // ⚠ 同上：静默吞异常。
        }


    }
}
