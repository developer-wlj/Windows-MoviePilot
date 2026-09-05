using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// Nginx 配置端口修改与重载：前端监听端口 + 后端 upstream 端口。
    /// 修改前自动备份原配置，nginx 运行中则执行 -s reload 让新端口生效。
    /// </summary>
    public static class NginxConfigService
    {
        // UTF-8 无 BOM 编码：.NET Framework 的 Encoding.UTF8 写入时会加 BOM（EF BB BF），
        // nginx 对配置文件开头的 BOM 敏感（可能报 unknown directive），故读写统一使用无 BOM 编码
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// 定位 nginx 配置文件（按优先级）：
        /// 1. 面板配置目录下的 nginx.conf（bin\config，与面板配置同目录，最高优先）；
        /// 2. nginx 安装目录 conf\ 下的 nginx.conf；
        /// 3. 回退 conf\vhosts 下含 listen 指令的实际配置文件（如 mp_v2.conf）。
        /// 返回 null 表示未找到。
        public static string FindConfigFile()
        {
            // 1. 面板配置目录下的 nginx.conf
            string panelTemplate = Path.Combine(AppConfig.CONFIG_DIR, "nginx.conf");
            if (File.Exists(panelTemplate))
            {
                return panelTemplate;
            }

            // 2. nginx 安装目录 conf 下的模板文件
            string confDir = Path.Combine(AppConfig.NGINX_DIR, "conf");
            if (!Directory.Exists(confDir))
            {
                return null;
            }

            string template = Path.Combine(confDir, "nginx.conf");
            if (File.Exists(template))
            {
                return template;
            }

            // 3. 回退：vhosts 目录下含 listen 指令的配置文件
            string vhostsDir = Path.Combine(confDir, "vhosts");
            if (Directory.Exists(vhostsDir))
            {
                foreach (string file in Directory.GetFiles(vhostsDir, "*.conf"))
                {
                    string content = File.ReadAllText(file, Utf8NoBom);
                    if (content.IndexOf("listen", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return file;
                    }
                }
            }
            return null;
        }

        /// 应用端口配置：修改 nginx 配置（自动备份 .bak），nginx 运行中则重载生效。
        /// 返回是否成功（失败原因写入日志）。
        public static bool ApplyPorts(int nginxPort, int backendPort, Action<string> log)
        {
            string configFile = FindConfigFile();
            if (configFile == null)
            {
                log("错误: 未找到 nginx 配置文件（" + Path.Combine(AppConfig.NGINX_DIR, "conf") + "）");
                return false;
            }

            string content;
            try
            {
                content = File.ReadAllText(configFile, Utf8NoBom);
            }
            catch (Exception ex)
            {
                log("读取 nginx 配置失败: " + ex.Message);
                return false;
            }

            // 修改前备份原始配置，便于回滚
            try
            {
                File.WriteAllText(configFile + ".bak", content, Utf8NoBom);
            }
            catch (Exception ex)
            {
                log("备份 nginx 配置失败: " + ex.Message);
            }

            string updated = content;

            // 1. 前端监听端口：listen 3000; 与 listen [::]:3000;
            updated = Regex.Replace(updated, @"listen\s+\[::\]:\d+", "listen [::]:" + nginxPort);
            updated = Regex.Replace(updated, @"listen\s+\d+;", "listen " + nginxPort + ";");

            // 2. 后端端口：upstream backend_api 块内的 server 127.0.0.1:PORT;
            // 注意：替换模式必须用 ${1} 显式界定组号，否则 $1 后紧跟端口数字会被解析成不存在的组号（如 $13111），
            // .NET 对不存在的组号原样保留字面量，导致整个 upstream 块被替换成垃圾文本。
            updated = Regex.Replace(updated,
                @"(upstream\s+backend_api\s*\{[^}]*server\s+127\.0\.0\.1:)\d+",
                "${1}" + backendPort);

            // 3. 后端端口：所有 proxy_pass http://127.0.0.1:PORT;
            updated = Regex.Replace(updated,
                @"(proxy_pass\s+http://127\.0\.0\.1:)\d+",
                "${1}" + backendPort);

            if (updated == content)
            {
                log("nginx 配置无需修改（端口与现有配置一致）: " + configFile);
            }
            else
            {
                try
                {
                    File.WriteAllText(configFile, updated, Utf8NoBom);
                    log("已更新 nginx 配置: " + configFile);
                    log("  监听端口: " + nginxPort + "，后端端口: " + backendPort + "（原配置备份为 .bak）");
                }
                catch (Exception ex)
                {
                    log("写入 nginx 配置失败: " + ex.Message);
                    return false;
                }
            }

            // 模板已更新：先同步到 nginx 实际加载的 conf\ 目录，reload 才能生效
            EnvironmentSetup.SyncNginxConfigs(log);
            ReloadNginx(log);
            return true;
        }

        /// nginx 运行中时执行 -s reload 使新端口生效。
        private static void ReloadNginx(Action<string> log)
        {
            if (!ServiceManager.IsRunning("nginx"))
            {
                log("Nginx 未运行，端口配置将在下次启动服务时生效");
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(AppConfig.NGINX_DIR, "nginx.exe"),
                    Arguments = "-s reload",
                    WorkingDirectory = AppConfig.NGINX_DIR,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.EnvironmentVariables["PATH"] = AppConfig.BuildEnvPath();
                Process.Start(psi);
                log("已重载 Nginx，新端口生效。修改后端端口，需重启服务");
            }
            catch (Exception ex)
            {
                log("重载 Nginx 失败: " + ex.Message);
            }
        }
    }
}
