using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// 面板自更新：查询开发者仓库（developer-wlj/Windows-MoviePilot）GitHub Release 的最新版本，
    /// 与当前面板版本比较；确认后下载新版 MoviePilot-V3.exe 到 TMP_DIR，把当前 exe 改名
    /// MoviePilot-V3-old.exe 后移入运行目录（调用方随后重启面板，旧 exe 由新版启动时清理）。
    /// 网络请求由 .NET 内置 HttpWebRequest 完成（EnvironmentSetup 程序集下载器，不再依赖系统 curl
    /// 组件，任意 Windows 版本可用），支持配置的 GitHub Token 与代理（系统代理 / 手动 http / 关闭）。
    /// </summary>
    public static class PanelUpdateService
    {
        // 面板仓库与 Release 资产名（GitHub Actions 推送 v* 标签自动发布 MoviePilot-V3.exe）
        private const string ReleaseLatestApi = "https://api.github.com/repos/developer-wlj/Windows-MoviePilot/releases/latest";
        private const string ReleaseDownloadBase = "https://github.com/developer-wlj/Windows-MoviePilot/releases/download/";
        private const string AssetName = "MoviePilot-V3.exe";
        // 旧版 exe 备份名（当前进程运行中无法删除，只能改名；新版启动后清理）
        private const string OldExeName = "MoviePilot-V3-old.exe";

        /// 解析 GitHub API JSON 中的 "tag_name": "v1.0.4" 字段。
        private static readonly Regex TagNameRegex = new Regex("\"tag_name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>查询仓库最新 Release 的 tag_name（如 v1.0.4）；请求失败或解析不到时返回 null。</summary>
        public static string FetchLatestTag(Action<string> log)
        {
            string json = EnvironmentSetup.HttpGetText(ReleaseLatestApi, log, true, 30);
            if (json == null)
            {
                log.Warn("查询面板最新版本失败，请检查网络 / 代理 / GitHub Token");
                return null;
            }
            Match m = TagNameRegex.Match(json);
            if (!m.Success)
            {
                log.Warn("未从 GitHub API 响应中解析到 tag_name，请检查网络或稍后重试（响应可能被限流/拦截）");
                return null;
            }
            return m.Groups[1].Value.Trim();
        }

        /// <summary>远端标签（形如 v1.0.4）是否高于当前面板版本；格式非法时返回 false。</summary>
        public static bool IsNewerThanCurrent(string tag)
        {
            int[] latest = ParseVersion(tag);
            int[] current = ParseVersion(AppConfig.APP_VERSION);
            if (latest == null || current == null)
            {
                return false;
            }
            for (int i = 0; i < 3; i++)
            {
                if (latest[i] != current[i])
                {
                    return latest[i] > current[i];
                }
            }
            return false; // 相同（或本地更高）：无更新
        }

        /// <summary>
        /// 下载指定版本的面板 exe 到 TMP_DIR（文件名带版本号防与运行目录混淆），
        /// 下载完成校验可执行文件头（防代理拦截返回 HTML）。成功返回本地路径，失败返回 null。
        /// </summary>
        public static string DownloadAsset(string tag, Action<string> log)
        {
            if (ParseVersion(tag) == null)
            {
                log.Error("版本标签格式非法，拒绝下载: " + tag);
                return null;
            }
            string destFile = Path.Combine(AppConfig.TMP_DIR, "MoviePilot-V3." + tag + ".exe");
            TryDeleteFile(destFile); // 清理上次下载失败的残留
            string url = ReleaseDownloadBase + tag + "/" + AssetName;
            log.Info("正在下载面板 " + tag + " ...");
            if (!EnvironmentSetup.HttpDownloadToFile(url, destFile, log, true, 600))
            {
                // 失败时 HttpDownloadToFile 已写日志并删除残留文件
                return null;
            }
            if (!IsValidExe(destFile))
            {
                log.Error("下载的文件校验失败（不是有效的可执行文件，可能被代理/网络拦截），已删除");
                TryDeleteFile(destFile);
                return null;
            }
            return destFile;
        }

        /// <summary>
        /// 安装新版面板：当前运行 exe 改名 MoviePilot-V3-old.exe → 新版 exe 移入运行目录。
        /// 返回错误信息，null 表示成功（调用方随后启动新 exe 并退出当前进程；
        /// 当前进程仍占用 old exe，其删除留待新版启动时进行）。
        /// 任一步失败自动回滚恢复旧 exe，不破坏现状。
        /// </summary>
        public static string InstallUpdate(string downloadedFile, Action<string> log)
        {
            string exePath;
            try
            {
                exePath = Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                exePath = null;
            }
            string expectExe = Path.Combine(AppConfig.BASE_DIR, AssetName);
            if (!string.Equals(exePath, expectExe, StringComparison.OrdinalIgnoreCase))
            {
                return "当前运行程序不是运行目录下的 " + AssetName + "（实际: " + (exePath ?? "未知") + "），已取消自更新。";
            }

            string oldPath = Path.Combine(AppConfig.BASE_DIR, OldExeName);
            TryDeleteFile(oldPath); // 清理上次更新失败的残留（可能被占用删除失败，尽力而为）
            if (File.Exists(oldPath))
            {
                // 残留被占用无法删除（如上次更新后旧进程尚未退出）：换名腾出目标位置，不阻塞本次更新
                try { File.Move(oldPath, oldPath + "." + Guid.NewGuid().ToString("N")); } catch { }
            }
            try
            {
                // 运行中的 exe 无法删除但允许改名：先腾出原名
                File.Move(exePath, oldPath);
                log.Info("旧版面板已改名 " + OldExeName + "（新版启动后自动清理）");
            }
            catch (Exception ex)
            {
                return "改名当前面板 exe 失败（文件可能被占用，请关闭杀毒软件后重试）: " + ex.Message;
            }
            try
            {
                File.Move(downloadedFile, exePath);
            }
            catch (Exception ex)
            {
                // 回滚：把旧 exe 恢复为原名，保证面板仍可正常运行
                try
                {
                    if (File.Exists(oldPath) && !File.Exists(exePath))
                    {
                        File.Move(oldPath, exePath);
                        log.Warn("已回滚恢复原面板 exe");
                    }
                }
                catch
                {
                }
                return "替换面板 exe 失败: " + ex.Message;
            }
            return null;
        }

        /// <summary>尝试删除运行目录下遗留的旧版 exe（新版面板启动后调用，旧进程退出后文件锁已释放）。</summary>
        public static void TryDeleteOldExe()
        {
            TryDeleteFile(Path.Combine(AppConfig.BASE_DIR, OldExeName));
        }

        // ---- 私有辅助 ----

        /// <summary>解析面板版本标签（形如 v1.0.4，v 前缀可选，1~3 段纯数字）；非法返回 null。</summary>
        private static int[] ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return null;
            }
            string num = tag.Trim().TrimStart('v', 'V');
            if (num.Length == 0)
            {
                return null;
            }
            string[] seg = num.Split('.');
            if (seg.Length < 1 || seg.Length > 3)
            {
                return null;
            }
            int[] parts = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (i >= seg.Length)
                {
                    parts[i] = 0;
                    continue;
                }
                int v;
                if (!int.TryParse(seg[i], out v) || v < 0)
                {
                    return null;
                }
                parts[i] = v;
            }
            return parts;
        }

        /// <summary>exe 是 PE 文件：校验 DOS 头 MZ 与最小体积（防下载到 HTML 错误页）。</summary>
        private static bool IsValidExe(string file)
        {
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length < 50 * 1024)
                {
                    return false;
                }
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    return fs.ReadByte() == 0x4D && fs.ReadByte() == 0x5A;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>重试删除文件（下载失败 / 更新残留清理，文件锁可能延迟释放）。</summary>
        private static void TryDeleteFile(string file)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (file != null && File.Exists(file))
                    {
                        File.Delete(file);
                    }
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(500);
                }
            }
        }
    }
}
