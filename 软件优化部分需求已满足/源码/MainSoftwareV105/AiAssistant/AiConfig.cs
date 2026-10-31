using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MainSoftwareV100
{
    /// <summary>
    /// AI助手配置（静态类，不用 new 直接用）。
    /// 从 exe 目录下的 "AI Config.txt" 读取，换一家API或换模型只改txt，不用重新编译。
    ///
    /// 文件格式（一行一项，等号分隔，#开头为注释）：
    ///     api地址=https://api.moonshot.cn/v1
    ///     api密钥=sk-xxxxxxxx          ← 填明文即可，首次读取后自动加密改写为下行形式
    ///     api密钥密文=AQAAANCM...       ← DPAPI密文（按当前Windows用户加密，换用户/换电脑需重填）
    ///     模型名=moonshot-v1-8k
    ///     记忆条数=20
    ///     知识库目录=AI知识库
    ///
    /// 各家API地址示例：
    ///     Kimi     https://api.moonshot.cn/v1
    ///     DeepSeek https://api.deepseek.com/v1
    ///     本地Ollama（未来备用） http://127.0.0.1:11434/v1
    /// </summary>
    public static class AiConfig
    {
        public static string ApiBase = "";      //接口地址，如 https://api.moonshot.cn/v1
        public static string ApiKey = "";       //密钥
        public static string Model = "";        //模型名
        public static int MemoryTurns = 20;     //记忆条数（系统消息不算，超出后丢最旧的）
        public static string KbDir = "AI知识库";//知识库文件夹名（exe目录下）

        public static string FilePath
        {
            get
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(exeDir, "AI Config.txt");
            }
        }

        /// <summary>读取配置。文件不存在或缺项时给出默认值并返回false。</summary>
        public static bool Load()
        {
            if (!File.Exists(FilePath)) return false;
            string plainKey = "";
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                string key = s.Substring(0, eq).Trim();
                string val = s.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "api地址": ApiBase = val; break;
                    case "api密钥": plainKey = val; break;
                    case "api密钥密文":
                        //DPAPI解密失败（换了Windows用户或机器）会返回空，落到下面的缺项判断
                        string dec = Unprotect(val);
                        if (dec.Length > 0) ApiKey = dec;
                        break;
                    case "模型名": Model = val; break;
                    case "记忆条数": int.TryParse(val, out MemoryTurns); break;
                    case "知识库目录": KbDir = val; break;
                }
            }

            //明文密钥迁移：填了明文就加密改写文件，之后磁盘上不再出现明文。
            //明文优先于密文——用户换密钥时改的是明文行，不能因为旧密文还在就忽略新密钥
            if (plainKey.Length > 0 && !plainKey.Contains("在这里粘贴"))
            {
                ApiKey = plainKey;
                MigratePlainKey(plainKey);
            }
            return ApiBase.Length > 0 && ApiKey.Length > 0 && Model.Length > 0;
        }

        //============ DPAPI加解密（System.Security，按当前Windows用户加密）============

        private static string Protect(string plain)
        {
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        private static string Unprotect(string cipher)
        {
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }

        /// <summary>把配置文件里的明文密钥行替换为密文行（旧密文行一并丢弃，避免两行不一致），其余行原样保留。失败静默（下次启动再迁）。</summary>
        private static void MigratePlainKey(string plain)
        {
            try
            {
                string enc = Protect(plain);
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                StringBuilder sb = new StringBuilder();
                bool replaced = false;
                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (!replaced && t.StartsWith("api密钥="))
                    {
                        sb.AppendLine("api密钥密文=" + enc);
                        replaced = true;
                    }
                    else if (t.StartsWith("api密钥密文=")) continue;   //丢弃旧密文
                    else sb.AppendLine(line);
                }
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>生成一份模板文件（首次启动或配置缺失时调用）。</summary>
        public static void CreateTemplate()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# AI助手配置文件：等号左边是键名不要改，右边填你的值");
            sb.AppendLine("# 以#开头的是注释行，不生效");
            sb.AppendLine("# api密钥填明文即可，软件首次读取后会自动加密改写成 api密钥密文（按当前Windows用户加密，换电脑需重填）");
            sb.AppendLine("api地址=https://api.moonshot.cn/v1");
            sb.AppendLine("api密钥=在这里粘贴你的API密钥");
            sb.AppendLine("模型名=moonshot-v1-8k");
            sb.AppendLine("记忆条数=20");
            sb.AppendLine("知识库目录=AI知识库");
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
