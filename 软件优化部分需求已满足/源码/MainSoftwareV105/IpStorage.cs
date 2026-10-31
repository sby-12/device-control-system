using System;
using System.Collections.Generic;   // Dictionary 字典
using System.IO;                    // 文件读写
using System.Reflection;            // Assembly：定位 exe 所在目录
using System.Text;                  // StringBuilder / Encoding

namespace MainSoftwareV100
{
    /// <summary>
    /// IP 存档工具（静态类，不用 new 直接用）。
    /// 职责：把四台仪器的 机型+IP+端口 存到 exe 目录下的 "IP Storage.txt"，
    ///       下次启动软件时读回来，自动填回连接弹窗的下拉框和输入框。
    ///
    /// 文件格式（一行一台仪器，等号分隔，逗号三段）：
    ///     电流源A=NGI N2410,192.168.1.12,502
    ///     电流源B=NGI N36200,192.168.1.13,502
    ///     示波器=RIGOL MHO984,192.168.1.10,5555
    ///     信号源=信号源（原机型）,192.168.1.11,5555
    ///
    /// 2026-10-30 升级：值从"IP,端口"两段升级为"机型,IP,端口"三段；
    /// 旧档只有两段时读得出 IP/端口，机型留空由连接窗口补默认值（向后兼容，不逼用户重填）。
    /// </summary>
    public static class IpStorage
    {
        // ===== 四台仪器在存档里的"键名"（等号左边）=====
        // 2026-10-30 起电源改为按槽位存档（电流源A/B 各选各的机型），
        // 旧的 N2410/N36200 键只用于把老存档迁到新键。
        public const string KEY_POWER_A = "电流源A";
        public const string KEY_POWER_B = "电流源B";
        public const string KEY_SCOPE   = "示波器";
        public const string KEY_SIGNAL  = "信号源";

        public const string KEY_N2410  = "N2410";    //旧键（存档迁移用）
        public const string KEY_N36200 = "N36200";   //旧键（存档迁移用）

        /// <summary>
        /// 存档文件的完整路径（和 exe 放同一目录）。
        /// 写成只读属性：每次用到时现拼，保证 exe 在哪文件就在哪。
        /// </summary>
        static string FilePath
        {
            get
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(exeDir, "IP Storage.txt");
            }
        }

        /// <summary>
        /// 读整个存档：返回 "键名 → IP,端口" 的字典。
        /// 文件不存在、某行格式不对，都不报错——跳过即可，不影响其他仪器。
        /// </summary>
        public static Dictionary<string, string> ReadAll()
        {
            var result = new Dictionary<string, string>();
            if (!File.Exists(FilePath)) return result;   // 第一次运行还没有存档，返回空表

            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string[] kv = line.Split('=');
                if (kv.Length == 2 && !string.IsNullOrWhiteSpace(kv[0]))
                {
                    result[kv[0].Trim()] = kv[1].Trim();
                }
            }
            return result;
        }

        /// <summary>
        /// 保存一台仪器的 机型+IP+端口。
        /// 思路：先读旧档（保住其他三台的数据）→ 改这一项 → 整体写回。
        /// 整个文件就 4 行，重写最简单，没有性能问题。
        /// </summary>
        public static void Save(string key, string model, string ip, int port)
        {
            var all = ReadAll();                        // 读旧档
            all[key] = model + "," + ip + "," + port;   // 覆盖/新增这一项

            var sb = new StringBuilder();
            foreach (var kv in all)
            {
                sb.AppendLine(kv.Key + "=" + kv.Value);
            }
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 读一台仪器的 机型+IP+端口。返回 false 表示没存过或格式坏。
        /// 兼容旧档：只有 "IP,端口" 两段时 model 返回空串，由调用方补默认机型。
        /// </summary>
        public static bool TryGet(string key, out string model, out string ip, out int port)
        {
            model = "";
            ip = "";
            port = 0;
            var all = ReadAll();
            if (!all.TryGetValue(key, out string value)) return false;

            string[] parts = value.Split(',');
            if (parts.Length == 3)
            {
                model = parts[0].Trim();
                ip = parts[1].Trim();
                return int.TryParse(parts[2].Trim(), out port);
            }
            if (parts.Length == 2)   //旧格式：没有机型段
            {
                ip = parts[0].Trim();
                return int.TryParse(parts[1].Trim(), out port);
            }
            return false;
        }
    }
}
