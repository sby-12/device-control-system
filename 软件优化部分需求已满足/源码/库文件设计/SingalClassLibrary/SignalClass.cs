using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
using InstrumentInterfaceLibrary;

namespace SingalClassLibrary
{
    //2026-10-30 实现 ISignalSource 统一接口（方案A·接口隔离），方法体一行未动
    public class SignalClass : ISignalSource
    {
        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "信号源（原机型）";

        public TcpTranslateClass translateClass;
        public bool OpenData = false;
        private int LenData = 800;

        //2026-10-31 µs→任意波点数换算系数（点/µs），2026-10-31 由主窗口字面量 8.12 移入本类。
        //【警示】这是学长原始代码里的经验值，无任何文档依据，未经真机标定。
        //官网 DG4000 数据手册只规定任意波 16k 点、500 MSa/s 采样率，"每 µs 多少点"取决于
        //播放配置：PER（周期）模式下 = 下载波形点数 ÷ 周期µs；SRAT（采样率）模式下 = 采样率 ÷ 1e6。
        //当前 Init() 配置（PER 1000µs、LenData 800 点）理论值约 0.8，与 8.12 相差 10 倍，
        //说明它对应的是另一套历史配置。真机必须用示波器核对脉宽后再修正，勿盲改。
        private const double PulsePointsPerUs = 8.12;

        /// <summary>微秒换算成下载波形的点数（接口约定传物理时间，本类内部换算）。</summary>
        private static int UsToPoints(double us)
        {
            return (int)(us * PulsePointsPerUs);
        }

        public SignalClass()
        {
            translateClass = new TcpTranslateClass();
        }

        //通信连接
        public void OpenTranslate(string host, int port)
        {
            //2026-10-31 修复3：Init 要发 SCPI 初始化指令，必须等 TCP 真正建链后才执行（原来在 AttachSignal 里
            //连接还没建立就调用，指令被静默丢弃）。挂到 AfterConnected，由库在连接成功时调用。
            translateClass.AfterConnected = Init;
            _ = translateClass.ConnectAsync(host, port);
        }

        //通信关闭
        public void CloseTranslate()
        {
            translateClass.CloseAsync();
        }


        /// <summary>
        /// 1. 正弦波
        /// </summary>
        public  double[] Sine(int points)
        {
            double[] data = new double[points];
            for (int i = 0; i < points; i++)
            {
                data[i] = (Math.Sin(2 * Math.PI * i / points) + 1) / 2;
             //   data[i] = (ushort)(v * 65535);
            }
            return data;
        }

        /// <summary>
        /// 2. 三角波
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
        /// 3. 锯齿波
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
        /// 4. 方波（可设置占空比）
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
        /// 5. PWM波（高电平窄脉冲）
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

        public void Init()
        {
            //自动触发
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:SHAP USER" + "\n"));
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:ARB:MODE FREQ" + "\n"));
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FREQ 1000" + "\n"));    // 1kHz
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT 5.0" + "\n"));     // 5Vpp
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS 0" + "\n")); // 0V偏移

            //手动触发
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:SHAP USER" + "\n"));
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:ARB:MODE SRAT" + "\n"));
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes("SOUR1:FUNC:ARB:SRAT 1000000" + "\n"));
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT 5.0" + "\n"));     // 5Vpp
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS 0" + "\n")); // 0V偏移
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1:LOAD INF" + "\n"));

            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:STAT ON" + "\n"));       // 打开脉冲串模式
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:MODE TRIG" + "\n"));    // 触发模式
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:NCYC 1" + "\n"));       // 每次触发只输出 1 次波形
            //                                                                                                        //外部触发
            ////    _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SOUR MAN" + "\n")); // 触发源 = 手动
            ////外部脉冲触发
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SOUR EXT" + "\n")); // 触发源 = 外部
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SLOP POS" + "\n"));    // 脉冲上升沿触发
            // _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 ON" + "\n"));
           

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes("*RST" + "\n"));
            Thread.Sleep(200);
            // SystemPlauseInit();
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC ARB" + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT 10" + "\n"));     // 5Vpp
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS 5" + "\n")); // 0V偏移
            Thread.Sleep(20);

            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1:IMP INF" + "\n"));

            //Thread.Sleep(20);
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes("SOUR1:FUNC:ARB 1000000" + "\n"));//:SOUR1:APPL:ARB 500

            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PER 1000us"));
            Thread.Sleep(20);
            // Thread.Sleep(20);
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:ARB:MODE FREQ" + "\n"));
            //Thread.Sleep(20);
            //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FREQ 1000000" + "\n"));
            //Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:MODE TRIG" + "\n"));     // 5Vpp
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG:SOUR MAN" + "\n")); // 触发源 = 手动
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS ON" + "\n")); // 触发源 = 手动
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:NCYC 1" + "\n"));
            Thread.Sleep(20);
            
        }

        //信号发生器赋值设置
        public void ValueSet(byte Value)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT " + Value.ToString()+ "\n"));
            Thread.Sleep(20);
            //2026-10-31 修复整数除法：原 (Value/2) 是 int 除法，设 5V 时偏移发 2 而非 2.5
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:VOLT:OFFS " + (Value / 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture)+"\n"));
            Thread.Sleep(20);
        }

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
        public void DownloadWave(int ch, double[] data)
        {
           // byte[] binaryData = ConvertDac16ToBinaryBlock(data);
            string cmd = $":SOUR{ch}:TRAC:DATA:DATA  VOLATILE," + string.Join(",", data);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(cmd+"\n"));
        }

        /// <summary>
        /// 将 DAC16 数组转换为仪器要求的二进制块格式
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

        public void Apply()
        {
            
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));// 触发源 = 手动
            
        }

        public void SinglePlaseSet(double widtime1)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:WIDT " + widtime1.ToString("0.000000") + "\n"));
            Thread.Sleep(20);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));

        }

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

        public void SinglePlaseSetTest(double num)
        {
       //     _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:WIDT 0.00002"+ "\n"));
       ////     _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:PULS:PER 0.0001" + "\n"));
       //     Thread.Sleep(50);
       //     _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:NCYC 2" + "\n"));
       //     Thread.Sleep(20);
       //     _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));
            //  Thread.Sleep(10);
            //  _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:PULS:WIDT 0.00005"+"\n"));
            //  //Thread.Sleep(20);
            ////  _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC:PULS:PER 0.0001" + "\n"));
            //  Thread.Sleep(50);
            //  _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:BURS:TRIG" + "\n"));

            OutputTwoDifferentPulses(200, 500, 300);

        }

        /// <summary>输出单个脉冲。pulse1Width 单位：微秒（接口约定），内部换算成任意波点数。</summary>
        public void OutputOneDifferentPulses(double pulse1Width)
        {
            int pulse1WidthPoints = UsToPoints(pulse1Width);

            try
            {
                // 2. 生成自定义双脉冲波形（两个不同宽度）
                //2026-10-31 修复 LenData 污染：改局部长度、不回写字段。原实现把 total 写进 LenData 字段
                //且播放周期 PER 固定 1000us，字段变大后点/µs 实际系数改变，后续单脉冲脉宽全部失真
                int len1 = Math.Max(pulse1WidthPoints, LenData);   //2026-10-31 局部长度：宽脉冲不再被 LenData 截断
                UInt16[] wave1 = new UInt16[len1];
                for (int i = 0; i < len1; i++)
                {
                    if (i < pulse1WidthPoints)
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
        }

        /// <summary>输出双脉冲：第一个脉冲宽度、第二个脉冲宽度、两脉冲间隔。
        /// 三个参数单位均为微秒（接口约定），内部统一换算成任意波点数。</summary>
        public void OutputTwoDifferentPulses(
           double pulse1Width,
           double pulse2Width,
           double interval)
        {
            int pw1 = UsToPoints(pulse1Width);
            int pw2 = UsToPoints(pulse2Width);
            int intervalPts = UsToPoints(interval);
           
            try
            {
                //_ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SOUR1:FUNC PULSe" + "\n"));
               // SystemOff();
                // 1. 计算总波形长度（2026-10-31 改局部变量：不回写 LenData 字段，避免污染后续单脉冲）
                int total = pw1 + intervalPts + pw2;
                int len = Math.Max(total, LenData);
                
                // 2. 生成自定义双脉冲波形（两个不同宽度）
                UInt16[] wave = new UInt16[len];
                for (int i = 0; i < len; i++)
                {
                    if (i < pw1)
                        wave[i] = 16383; // 第一个脉冲高电平
                    else if (i < pw1 + intervalPts)
                        wave[i] = 0;     // 间隔低电平
                    else if(i < total)
                        wave[i] = 16383; // 第二个脉冲（宽度不同）
                    else
                        wave[i] = 0;     // 尾部低电平收尾（原 i<=total 在 total==len 时吃掉收尾段）
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
        }


    }
}
