using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// 内置资源部署：app.ico 与 config 模板（nginx.conf / common.conf）作为嵌入资源打进 exe，
    /// 首次运行时释放到 exe 同级目录（层级平层：app.ico、config\nginx.conf、config\common.conf
    /// 与 MoviePilot-V3.exe 位于同一目录）。
    /// 目标文件已存在时不再释放（不覆盖用户本地修改），幂等可重复执行。
    /// </summary>
    public static class BundledResources
    {
        // 嵌入资源名前缀（RootNamespace，与 csproj 保持一致）
        private const string ResourcePrefix = "MoviePilot_V3.";

        /// 嵌入资源名 → 相对 BASE_DIR 的目标路径（层级平层）
        private static readonly Tuple<string, string>[] BundledFiles =
        {
            Tuple.Create("app.ico", "app.ico"),
            Tuple.Create("config.nginx.conf", "config\\nginx.conf"),
            Tuple.Create("config.common.conf", "config\\common.conf")
        };

        /// 释放全部内置资源到 exe 同级目录（存在性检查：已存在跳过，缺失补齐）。
        public static void Deploy()
        {
            foreach (Tuple<string, string> entry in BundledFiles)
            {
                DeployOne(entry.Item1, entry.Item2);
            }
        }

        /// 目标文件不存在时从嵌入资源写入；已存在时跳过（不覆盖）。
        private static void DeployOne(string resourceName, string relativePath)
        {
            string targetPath = Path.Combine(AppConfig.BASE_DIR, relativePath);
            if (File.Exists(targetPath))
            {
                return; // 已释放过：保留现有文件（含用户本地修改）
            }
            try
            {
                using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + resourceName))
                {
                    if (src == null)
                    {
                        Debug.WriteLine("嵌入资源不存在: " + resourceName);
                        return;
                    }
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    using (FileStream dst = File.Create(targetPath))
                    {
                        src.CopyTo(dst);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("释放内置资源失败 " + resourceName + ": " + ex.Message);
            }
        }
    }
}
