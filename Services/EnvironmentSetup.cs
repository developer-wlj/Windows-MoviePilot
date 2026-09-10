using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// 首次运行环境准备：下载便携版 Nginx / Git / Python 3.14.7 / tar（bsdtar）、同步 nginx 配置到安装目录、
    /// 建立 Python 虚拟环境（后端运行在 venv 中）、下载站点资源（GitHub raw，支持 Token）。
    /// HTTP 请求全部由 .NET 内置 HttpWebRequest 完成（不再依赖系统 curl 组件）；zip 由程序集直接解压，
    /// tar.gz 用 bsdtar 解压（环境准备阶段检测系统内置，缺失时自动下载 libarchive 部署到 runtime\tar）。
    /// 代理支持：系统代理（默认，读 Windows Internet 设置）/ 手动 http / 关闭；
    /// git 不读系统代理，使用 git 前由 ApplyGitProxy 把当前代理写入 git 全局配置（关闭时清空）。
    /// 内部均做存在性检查，已就绪时零下载、幂等可重复执行。
    /// </summary>
    public static class EnvironmentSetup
    {
        // ---- 便携版下载地址 ----
        private const string NginxVersion = "1.30.4";
        private const string NginxDownloadUrl = "https://nginx.org/download/nginx-1.30.4.zip";
        // 注意 tag 是 v2.49.0.windows.1（git-for-windows 的 release tag 带 .windows.N 后缀）
        private const string GitDownloadUrl = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.4/MinGit-2.55.0.4-64-bit.zip";
        private const string PythonDownloadUrl = "https://github.com/astral-sh/python-build-standalone/releases/download/20260814/cpython-3.14.7+20260814-x86_64-pc-windows-msvc-install_only.tar.gz";
        // freethreaded 版（MoviePilot-V3-T）专用解释器：free-threaded（无 GIL）构建
        private const string PythontDownloadUrl = "https://github.com/astral-sh/python-build-standalone/releases/download/20260814/cpython-3.14.7+20260814-x86_64-pc-windows-msvc-freethreaded-install_only.tar.gz";

        // ---- uv 便携版（官方依赖管理工具，版本与官方 Docker 基准一致） ----
        private const string UvVersion = "0.12.5";
        private const string UvDownloadUrl = "https://github.com/astral-sh/uv/releases/download/0.12.5/uv-x86_64-pc-windows-msvc.zip";

        // ---- 便携版 bsdtar（tar）：Windows 10 1803 / Server 2019 起系统自带（System32\tar.exe，libarchive 编译）；
        // 更旧系统（如 Server 2016）缺失时自动下载 libarchive msvc-x64 static 包，
        // 把 bin\bsdtar.exe 部署为 runtime\tar\tar.exe（DLL 依赖同目录整体带出） ----
        private const string TarVersion = "3.8.9";
        private const string TarDownloadUrl = "https://github.com/li-ruijie/libarchive/releases/download/v3.8.9/libarchive-v3.8.9-windows-msvc-x64-static.zip";

        // PAC 脚本（AutoConfigURL）按目标 URL 动态返回代理；git 全局 http.proxy 与后端环境变量注入
        // 只能携带一个固定代理地址，无目标 URL 可带时以 GitHub 为探测目标解析 PAC（面板 git / 资源下载主要指向 GitHub）
        private const string PacProbeUrl = "https://github.com/";

        // ---- GitHub 资源（站点数据，缺失或不完整将无法启动后端）----
        private const string SitesPydUrl = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources.v3/sites.cp314-win_amd64.pyd";
        private const string SitesBinUrl = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources.v3/user.sites.v3.bin";
        // freethreaded 版（MoviePilot-V3-T）的站点资源 pyd（cp314t 后缀，与 T 版解释器匹配）
        private const string SitesPydUrl_t = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources.v3/sites.cp314t-win_amd64.pyd";

        // 站点资源 pyd 文件名按解释器版本区分：标准版 cp314（带 GIL）、freethreaded 版 cp314t；
        // Python 只加载版本后缀匹配的扩展，下载与就绪检查必须与当前运行版本一致
        public static string SitesPydFileName
        {
            get { return AppConfig.IsTVersion ? "sites.cp314t-win_amd64.pyd" : "sites.cp314-win_amd64.pyd"; }
        }
        public static string SitesPydFileUrl
        {
            get { return AppConfig.IsTVersion ? SitesPydUrl_t : SitesPydUrl; }
        }
        // 前端发行包（MoviePilot-Frontend release 的 dist.zip，版本取自后端 version.py 的 FRONTEND_VERSION）
        private const string FrontendReleaseBase = "https://github.com/jxxghp/MoviePilot-Frontend/releases/download/";

        /// <summary>完整初始化流程：代理 → tar → nginx → git → python → 配置同步 → 代码（clone+补丁）→ venv（依赖）→ 站点资源。</summary>
        public static void EnsureEnvironment(Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.TMP_DIR); // 下载临时目录（BASE_DIR\tmp）
            }
            catch
            {
            }
            // 每轮环境准备重置 tar 失败标记：上一轮部署失败（如临时断网）后，再次点"启动服务" /
            // "修复运行环境"会重新尝试检测 / 部署，不必重启面板（tarExePath 成功后保持缓存不再检测）
            tarUnavailable = false;
            // 先确保 bsdtar（tar）：Python 等 .tar.gz 解压依赖它；系统缺失时自动下载 libarchive 便携版
            EnsureTar(log);
            // git 不读 Windows 系统代理：把当前代理（系统代理 / 手动 http）写入 git 全局配置，关闭时清空
            ApplyGitProxy(log);
            EnsureNginx(log);
            EnsureGit(log);
            EnsurePython(log);
            EnsureUv(log);
            SyncNginxConfigs(log);
            // 先确保后端代码（克隆官方 v3 + 打 v3-rebase 补丁）：依赖安装（requirements.txt）与站点资源（SITE_DIR）都位于代码目录内
            EnsureCode(log);
            EnsureVenv(log);
            EnsureSiteFiles(log);
        }

        /// <summary>
        /// 确保后端代码存在：目录无 .git 时克隆官方 v3 分支并打 v3-rebase 补丁（幂等）。
        /// 站点资源目录（server\app\application\site）在代码仓库内，必须放在站点资源下载之前。
        /// </summary>
        private static void EnsureCode(Action<string> log)
        {
            if (Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, ".git")))
            {
                return; // 代码已就绪
            }
            log.Info("未找到后端代码，开始克隆官方仓库并打补丁...");
            string error = UpgradeService.EnsureCode(log);
            if (error != null)
            {
                log.Error(error);
                log.Error("后端代码缺失，无法启动服务（请检查网络 / 代理 / GitHub Token 后重试）");
            }
            else
            {
                log.Info("后端代码就绪（官方 v3 + v3-rebase 补丁）");
                // 首次克隆后同步前端：按 version.py 的 FRONTEND_VERSION 对比本地版本，更高才下载覆盖
                EnsureFrontend(log);
            }
        }

        /// <summary>
        /// 首次克隆后端代码或升级后，读取 version.py 的 FRONTEND_VERSION，与 mp-web\version.txt
        /// 当前版本对比，后端要求版本更高时下载对应版本的前端发行包（dist.zip）到 tmp，
        /// 解压后去掉 dist 层整体强制覆盖到 mp-web 目录。
        /// force 为 true（勾选“更新时强制更新前端资源和后端认证和站点资源”）时，版本号相同也
        /// 重新下载覆盖：官方前端可能对同一版本号重新发布不同内容的 dist.zip（版本号不变、
        /// 内容更新），仅比较版本号会漏更；本地版本高于要求时即使 force 也不覆盖（用户自装的
        /// 更高版本前端不回退）。
        /// 版本支持 v3.0.1 / v3.0.1-1 / v3.0.1-beta01 等带后缀格式。
        /// </summary>
        public static void EnsureFrontend(Action<string> log, bool force = false)
        {
            string versionPy = Path.Combine(AppConfig.CurrentBackendDir, "version.py");
            string frontendVersion = ReadFrontendVersion(versionPy);
            if (frontendVersion == null)
            {
                log.Warn("未从 version.py 解析到 FRONTEND_VERSION，跳过前端资源下载");
                return;
            }

            // 对比 mp-web\version.txt：本地版本高于要求时跳过（用户自装的更高版本前端不覆盖）；
            // 版本相同且未勾选“强制更新资源”时也跳过——官方同一版本号可能重新发布不同内容，
            // 勾选后即使版本相同也重新下载覆盖（带后缀的版本号也可正确比较）
            string currentVersion = ReadVersionFile(Path.Combine(AppConfig.FRONTEND_DIR, "version.txt"));
            int versionCmp = currentVersion == null
                ? 1
                : CompareFrontendVersions(frontendVersion, currentVersion);
            if (versionCmp < 0)
            {
                log.Info("前端本地版本高于要求（本地 " + currentVersion + "，要求 " + frontendVersion + "），跳过下载");
                return;
            }
            if (versionCmp == 0)
            {
                if (!force)
                {
                    log.Info("前端已是最新（本地 " + currentVersion + "，要求 " + frontendVersion + "），跳过下载");
                    return;
                }
                log.Info("前端版本相同（" + currentVersion + "），按强制更新配置重新下载覆盖（官方同版本号可能更新内容）...");
            }
            else
            {
                log.Info("前端版本 " + (currentVersion ?? "未知") + " 低于要求 " + frontendVersion + "，开始下载...");
            }

            string archive = Path.Combine(AppConfig.TMP_DIR, "frontend-dist-" + frontendVersion + ".zip");
            string url = FrontendReleaseBase + frontendVersion + "/dist.zip";
            if (!DownloadFile(url, archive, log, true))
            {
                log.Warn("前端资源下载失败（可稍后通过\"检查MP更新\"触发升级重试）");
                return;
            }

            string extractDir = Path.Combine(AppConfig.TMP_DIR, "frontend-extract-" + Guid.NewGuid().ToString("N"));
            if (!ExtractArchive(archive, extractDir, log))
            {
                Cleanup(archive, extractDir);
                return;
            }
            // 压缩包内有一层 dist 目录，去掉该层后内容直接落到 mp-web
            string src = Path.Combine(extractDir, "dist");
            if (!Directory.Exists(src) || !File.Exists(Path.Combine(src, "index.html")))
            {
                log.Warn("前端压缩包结构异常（缺少 dist/index.html），已跳过覆盖");
                Cleanup(archive, extractDir);
                return;
            }
            try
            {
                // 强制覆盖：清空旧 mp-web 后整体移入 dist 内容，避免旧版本残留文件
                if (Directory.Exists(AppConfig.FRONTEND_DIR))
                {
                    Directory.Delete(AppConfig.FRONTEND_DIR, true);
                }
                Directory.CreateDirectory(AppConfig.FRONTEND_DIR);
                MoveContents(src, AppConfig.FRONTEND_DIR);
                log.Info("前端资源已更新到 " + frontendVersion + ": " + AppConfig.FRONTEND_DIR);
            }
            catch (Exception ex)
            {
                log.Error("覆盖前端资源失败: " + ex.Message);
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        /// <summary>应用或清空 git 全局代理（系统代理 / 手动 http；关闭或未配置代理时清空）。
        /// git 本身不读取 Windows 系统代理，故每次环境准备 / 使用 git 前调用本方法，
        /// 把当前生效的代理写入 git 全局配置（git config --global http.proxy）。</summary>
        public static void ApplyGitProxy(Action<string> log)
        {
            string gitExe = Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe");
            if (!File.Exists(gitExe))
            {
                return; // git 尚未就绪，就绪后由 EnsureEnvironment 再次应用
            }

            string proxyUrl = BuildProxyUrl();
            if (proxyUrl != null)
            {
                RunProcess(gitExe, "config --global http.proxy \"" + proxyUrl + "\"", AppConfig.GIT_DIR, null);
            }
            else
            {
                // 未设置过时 unset 返回非零，忽略
                RunProcess(gitExe, "config --global --unset-all http.proxy", AppConfig.GIT_DIR, null);
            }
        }

        /// <summary>站点资源是否就绪（缺失或不完整时后端无法启动）。</summary>
        public static bool SiteFilesReady()
        {
            try
            {
                // 3.14 时代官方资源为 sites.cp314-win_amd64.pyd（Python 只加载版本后缀匹配的扩展），
                // 早期 cp312 文件名是 3.12 时代的旧约定，版本升级后检查必须同步；
                // freethreaded 版（MoviePilot-V3-T）对应 cp314t 后缀
                string pydFile = Path.Combine(AppConfig.CurrentSiteDir, EnvironmentSetup.SitesPydFileName);
                string binFile = Path.Combine(AppConfig.CurrentSiteDir, "user.sites.v3.bin");
                return IsValidPyd(pydFile) && File.Exists(binFile) && new FileInfo(binFile).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // ==================== 便携版下载安装 ====================

        private static void EnsureNginx(Action<string> log)
        {
            if (File.Exists(Path.Combine(AppConfig.NGINX_DIR, "nginx.exe")))
            {
                return; // 已安装
            }
            log.Info("未找到Nginx，开始下载...");
            string archive = Path.Combine(AppConfig.TMP_DIR, "nginx-" + NginxVersion + ".zip");
            if (!DownloadFile(NginxDownloadUrl, archive, log, false))
            {
                return;
            }
            string extractDir = Path.Combine(AppConfig.TMP_DIR, "nginx-extract-" + Guid.NewGuid().ToString("N"));
            if (!ExtractArchive(archive, extractDir, log))
            {
                Cleanup(archive, extractDir);
                return;
            }
            string src = Path.Combine(extractDir, "nginx-" + NginxVersion);
            try
            {
                if (!Directory.Exists(src))
                {
                    log.Error("Nginx 压缩包结构异常");
                    return;
                }
                MoveContents(src, AppConfig.NGINX_DIR);
                log.Info("Nginx " + NginxVersion + " 安装完成: " + AppConfig.NGINX_DIR);
            }
            catch (Exception ex)
            {
                log.Error("安装 Nginx 失败: " + ex.Message);
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        private static void EnsureGit(Action<string> log)
        {
            if (File.Exists(Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe")))
            {
                return; // 已安装
            }
            log.Info("未找到便携版 Git，开始下载...");
            string archive = Path.Combine(AppConfig.TMP_DIR, "MinGit.zip");
            if (!DownloadFile(GitDownloadUrl, archive, log, false))
            {
                return;
            }
            string extractDir = Path.Combine(AppConfig.TMP_DIR, "mingit-extract-" + Guid.NewGuid().ToString("N"));
            if (!ExtractArchive(archive, extractDir, log))
            {
                Cleanup(archive, extractDir);
                return;
            }
            // MinGit 压缩包无顶层目录（cmd/etc/mingw64/usr 直接平铺），兼容两种结构
            string src = Path.Combine(extractDir, "MinGit-2.49.0-64-bit");
            if (!Directory.Exists(src))
            {
                src = extractDir;
            }
            try
            {
                MoveContents(src, AppConfig.GIT_DIR);
                log.Info("Git 便携版安装完成: " + AppConfig.GIT_DIR);
                ApplyGitProxy(log); // git 就绪后应用代理
            }
            catch (Exception ex)
            {
                log.Error("安装 Git 失败: " + ex.Message);
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        private static void EnsurePython(Action<string> log)
        {
            if (File.Exists(Path.Combine(AppConfig.CurrentPythonDir, "python.exe")))
            {
                return; // 已安装
            }
            // 标准版下载带 GIL 的解释器；freethreaded 版（MoviePilot-V3-T）下载 free-threaded 解释器
            string pythonDesc = AppConfig.IsTVersion ? "Python 3.14.7t（free-threaded）" : "Python 3.14.7";
            log.Info("未找到便携版 " + pythonDesc + "，开始下载...");
            string archive = Path.Combine(AppConfig.TMP_DIR, AppConfig.IsTVersion ? "python3147t.tar.gz" : "python3147.tar.gz");
            if (!DownloadFile(AppConfig.IsTVersion ? PythontDownloadUrl : PythonDownloadUrl, archive, log, false))
            {
                return;
            }
            string extractDir = Path.Combine(AppConfig.TMP_DIR, "python-extract-" + Guid.NewGuid().ToString("N"));
            if (!ExtractArchive(archive, extractDir, log))
            {
                Cleanup(archive, extractDir);
                return;
            }
            string src = Path.Combine(extractDir, "python");
            try
            {
                if (!Directory.Exists(src))
                {
                    log.Error("Python 压缩包结构异常");
                    return;
                }
                MoveContents(src, AppConfig.CurrentPythonDir);
                log.Info(pythonDesc + " 便携版安装完成: " + AppConfig.CurrentPythonDir);
            }
            catch (Exception ex)
            {
                log.Error("安装 Python 失败: " + ex.Message);
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        /// <summary>确保便携版 uv 就绪（官方依赖管理工具，用于 pyproject.toml + uv.lock 安装）。</summary>
        private static void EnsureUv(Action<string> log)
        {
            if (File.Exists(Path.Combine(AppConfig.UV_DIR, "uv.exe")))
            {
                return; // 已安装
            }
            log.Info("未找到便携版 uv " + UvVersion + "，开始下载...");
            string archive = Path.Combine(AppConfig.TMP_DIR, "uv-" + UvVersion + ".zip");
            if (!DownloadFile(UvDownloadUrl, archive, log, false))
            {
                return;
            }
            string extractDir = Path.Combine(AppConfig.TMP_DIR, "uv-extract-" + Guid.NewGuid().ToString("N"));
            if (!ExtractArchive(archive, extractDir, log))
            {
                Cleanup(archive, extractDir);
                return;
            }
            try
            {
                MoveContents(extractDir, AppConfig.UV_DIR);
                log.Info("uv " + UvVersion + " 安装完成: " + AppConfig.UV_DIR);
            }
            catch (Exception ex)
            {
                log.Error("安装 uv 失败: " + ex.Message);
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        /// <summary>把 CONFIG_DIR 的 nginx.conf / common.conf 同步到 NGINX_DIR\conf（内容不同才覆盖，
        /// 面板模板是权威源，覆盖官方默认配置）。nginx 实际加载的是 conf\ 目录下的文件，
        /// 端口等模板修改后必须同步，reload / 下次启动才会生效。</summary>
        public static void SyncNginxConfigs(Action<string> log)
        {
            foreach (string name in new[] { "nginx.conf", "common.conf" })
            {
                string src = Path.Combine(AppConfig.CONFIG_DIR, name);
                string dest = Path.Combine(AppConfig.NGINX_CONFIG_DIR, name);
                if (!File.Exists(src))
                {
                    log.Warn("未找到配置文件 " + src);
                    continue;
                }
                try
                {
                    bool needCopy = true;
                    if (File.Exists(dest))
                    {
                        needCopy = !FilesEqual(src, dest);
                    }
                    if (needCopy)
                    {
                        Directory.CreateDirectory(AppConfig.NGINX_CONFIG_DIR);
                        File.Copy(src, dest, true);
                        log.Info("已同步 " + name + " 到 " + AppConfig.NGINX_CONFIG_DIR);
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("同步 " + name + " 失败: " + ex.Message);
                }
            }
        }

        /// 两个文件是否内容一致（先比长度，再逐字节比较）。
        private static bool FilesEqual(string fileA, string fileB)
        {
            try
            {
                FileInfo fa = new FileInfo(fileA);
                FileInfo fb = new FileInfo(fileB);
                if (fa.Length != fb.Length)
                {
                    return false;
                }
                byte[] ba = File.ReadAllBytes(fileA);
                byte[] bb = File.ReadAllBytes(fileB);
                for (int i = 0; i < ba.Length; i++)
                {
                    if (ba[i] != bb[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ==================== Python 虚拟环境 ====================

        private static void EnsureVenv(Action<string> log)
        {
            string basePython = Path.Combine(AppConfig.CurrentPythonDir, "python.exe");
            if (!File.Exists(basePython))
            {
                log.Warn("Python 未就绪，跳过虚拟环境创建");
                return;
            }
            string venvPython = Path.Combine(AppConfig.CurrentVenvDir, "Scripts", "python.exe");
            // 有效 venv 需同时具备 python.exe 与 activate：activate 是 venv 创建完成的标志，
            // 创建中断会留下只有 python.exe 的半成品（无 activate），仅凭 python.exe
            // 存在会误判“已就绪”而跳过重建，导致依赖永远无法安装
            string venvActivate = Path.Combine(AppConfig.CurrentVenvDir, "Scripts", "activate");
            if (File.Exists(venvPython) && File.Exists(venvActivate))
            {
                // 便携版 Python 升级（如 3.12 → 3.14）后旧 venv 与新版不匹配：删除重建，
                // 避免用旧解释器跑新代码（依赖与 pyd 扩展均按新版本编译）
                if (!VenvMatchesCurrentPython())
                {
                    log.Warn("虚拟环境与当前 Python 版本不匹配，删除重建: " + AppConfig.CurrentVenvDir);
                    try
                    {
                        Directory.Delete(AppConfig.CurrentVenvDir, true);
                    }
                    catch (Exception ex)
                    {
                        log.Error("删除旧虚拟环境失败（可能被占用）: " + ex.Message);
                        return;
                    }
                }
                else
                {
                    // 已存在且匹配：确保 uv 已暴露到 venv（后端插件依赖安装依赖 find_uv）
                    return;
                }
            }
            else if (Directory.Exists(AppConfig.CurrentVenvDir))
            {
                // venv 目录残留但无效（如创建中断留下的半成品）：删除重建，避免残留文件导致创建失败
                log.Warn("清理无效虚拟环境残留: " + AppConfig.CurrentVenvDir);
                try
                {
                    Directory.Delete(AppConfig.CurrentVenvDir, true);
                }
                catch (Exception ex)
                {
                    log.Error("清理无效虚拟环境失败（可能被占用）: " + ex.Message);
                    return;
                }
            }
            log.Info("创建 Python 虚拟环境: " + AppConfig.CurrentVenvDir);
            // 用 RunProcessOutput 捕获创建输出：失败时日志直接给出 python 的错误原因（而非仅退出码）
            string uvExe = Path.Combine(AppConfig.UV_DIR, "uv.exe");
            if (!File.Exists(uvExe))
            {
                log.Warn("未找到便携版 uv，跳过依赖安装");
                return;
            }
            string venvOutput = RunProcessOutput(uvExe, "venv \"" + AppConfig.CurrentVenvDir + "\" -p \"" + AppConfig.GetPythonExe() + "\"", AppConfig.BIN_DIR, AppConfig.BuildEnvPath());
            if (!File.Exists(venvPython))
            {
                string detail = venvOutput.Trim();
                log.Error("创建虚拟环境失败: " + (detail.Length > 0 ? detail : "（无错误输出）"));
                return;
            }
            log.Info("虚拟环境创建完成");
            InstallRequirements(venvPython, log);
            
        }

        /// <summary>venv 的 pyvenv.cfg 是否指向当前便携版 Python（home 路径包含 PYTHON_DIR，忽略大小写）。</summary>
        private static bool VenvMatchesCurrentPython()
        {
            string cfg = Path.Combine(AppConfig.CurrentVenvDir, "pyvenv.cfg");
            if (!File.Exists(cfg)) return false;
            try
            {
                return File.ReadAllText(cfg).IndexOf(AppConfig.CurrentPythonDir, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>安装/更新后端 Python 依赖：优先 pip install -r requirements.txt（旧版兼容）；
        /// requirements.txt 缺失时改用 uv sync 按 pyproject.toml + uv.lock 安装（官方新版依赖管理）。</summary>
        public static void InstallRequirements(string pythonExe, Action<string> log)
        {
            string reqFile = Path.Combine(AppConfig.CurrentBackendDir, "requirements.txt");
            if (File.Exists(reqFile))
            {
                log.Info("安装 Python 依赖（首次可能耗时较长）...");
                // 配置了代理（系统代理 / http）时 pip 也走同一代理（--proxy），与 git / 下载保持一致
                string proxyArgs = "";
                string proxyUrl = BuildProxyUrl();
                if (proxyUrl != null)
                {
                    proxyArgs = "--proxy \"" + proxyUrl + "\" ";
                }
                // pip 在管道模式下默认按系统 ANSI 代码页（GBK）输出，中文会乱码；
                // PYTHONUTF8=1 强制 Python 以 UTF-8 输出，与 RunProcessOutput 的 UTF-8 解码配对
                Dictionary<string, string> pythonEnv = new Dictionary<string, string>
                {
                    { "PYTHONUTF8", "1" }
                };
                RunProcessOutput(pythonExe,
                    "-m pip install " + proxyArgs + "-r \"" + reqFile + "\" --upgrade", AppConfig.CurrentBackendDir, AppConfig.BuildEnvPath(), pythonEnv);
                return;
            }
            string pyProject = Path.Combine(AppConfig.CurrentBackendDir, "pyproject.toml");
            if (!File.Exists(pyProject))
            {
                log.Warn("未找到 requirements.txt / pyproject.toml，跳过依赖安装");
                return;
            }
            string uvExe = Path.Combine(AppConfig.UV_DIR, "uv.exe");
            if (!File.Exists(uvExe))
            {
                log.Warn("未找到便携版 uv，跳过依赖安装");
                return;
            }
            log.Info("安装 Python 依赖（uv sync，首次可能耗时较长）...");
            Dictionary<string, string> extraEnv = new Dictionary<string, string>
            {
                { "UV_PROJECT_ENVIRONMENT", AppConfig.CurrentVenvDir }
            };
            string proxy = BuildProxyUrl();
            if (proxy != null)
            {
                // uv 不认 pip 的 --proxy，走标准代理环境变量
                extraEnv["HTTP_PROXY"] = proxy;
                extraEnv["HTTPS_PROXY"] = proxy;
            }
            string args = "lock --no-cache --directory \"" + AppConfig.CurrentBackendDir + "\"";
            RunProcessOutput(uvExe, args, AppConfig.CurrentBackendDir, AppConfig.BuildEnvPath(), extraEnv);
            try
            {
                if (Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, "moviepilot.egg-info"))) 
                {
                    Directory.Delete(Path.Combine(AppConfig.CurrentBackendDir, "moviepilot.egg-info"), true);
                }
            }
            catch (Exception)
            {
            }
            // 与官方一致：按 uv.lock 锁定版本安装到面板 venv（不安装项目本身）
            if (AppConfig.CurrentBackendDir.EndsWith("-T"))
            {
                args = "sync --directory \"" + AppConfig.CurrentBackendDir + "\" --locked --no-default-groups --group runtime-free-threaded --no-dev --no-install-project";
                RunProcessOutput(uvExe, args, AppConfig.CurrentBackendDir, AppConfig.BuildEnvPath(), extraEnv);
            }
            else {
                args = "sync --directory \"" + AppConfig.CurrentBackendDir + "\" --locked --no-default-groups --no-dev --group runtime-standard --no-install-project";
                RunProcessOutput(uvExe, args, AppConfig.CurrentBackendDir, AppConfig.BuildEnvPath(), extraEnv);
            }
            
        }

        // ==================== 站点资源（GitHub raw，携带 Token） ====================

        /// <summary>确保认证 / 站点资源就绪（认证资源 sites.*.pyd、站点资源 user.sites.v3.bin）；force 为 true
        /// （download.flag 或“更新时强制更新”配置触发）时强制重新下载替换，
        /// 先备份旧文件，下载失败或校验不通过时恢复旧文件；全部成功返回 true。</summary>
        private static bool EnsureSiteFiles(Action<string> log, bool force = false)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.CurrentSiteDir);
            }
            catch (Exception ex)
            {
                log.Error("创建站点资源目录失败: " + ex.Message);
                return false;
            }

            bool ok = true;

            // pyd 文件名按当前运行版本：标准版 cp314 / freethreaded 版（MoviePilot-V3-T）cp314t
            string pydFile = Path.Combine(AppConfig.CurrentSiteDir, SitesPydFileName);
            if (!force && IsValidPyd(pydFile))
            {
                
            }
            else
            {
                string pydBackup;
                if (!TryBackupSiteFile(pydFile, force, out pydBackup, log))
                {
                    log.Error("备份 " + SitesPydFileName + " 失败，已跳过更新（保留原文件）");
                    ok = false;
                }
                else if (!DownloadFile(SitesPydFileUrl, pydFile, log, true))
                {
                    RestoreSiteFile(pydBackup, pydFile, log);
                    log.Error("下载 " + SitesPydFileName + " 失败，已保留原文件（请检查网络 / GitHub Token）");
                    ok = false;
                }
                else if (!IsValidPyd(pydFile))
                {
                    RestoreSiteFile(pydBackup, pydFile, log);
                    log.Error(SitesPydFileName + " 文件不完整，已恢复原文件");
                    ok = false;
                }
                else
                {
                    TryDelete(pydBackup);
                    log.Info(force ? "认证资源已更新: " + SitesPydFileName : "站点资源下载完成: " + SitesPydFileName);
                }
            }

            string binFile = Path.Combine(AppConfig.CurrentSiteDir, "user.sites.v3.bin");
            if (!force && File.Exists(binFile) && new FileInfo(binFile).Length > 0)
            {
                
            }
            else
            {
                string binBackup;
                if (!TryBackupSiteFile(binFile, force, out binBackup, log))
                {
                    log.Error("备份 user.sites.v3.bin 失败，已跳过更新（保留原文件）");
                    ok = false;
                }
                else if (!DownloadFile(SitesBinUrl, binFile, log, true))
                {
                    RestoreSiteFile(binBackup, binFile, log);
                    log.Error("下载 user.sites.v3.bin 失败，已保留原文件（请检查网络 / GitHub Token）");
                    ok = false;
                }
                else
                {
                    TryDelete(binBackup);
                    log.Info(force ? "站点资源已更新: user.sites.v3.bin" : "站点资源下载完成: user.sites.v3.bin");
                }
            }

            return ok;
        }

        /// <summary>强制刷新认证 / 站点资源（download.flag 或“更新时强制更新”配置触发）：备份旧文件后重新下载，失败时恢复旧文件；全部成功返回 true。</summary>
        public static bool RefreshSiteFiles(Action<string> log)
        {
            return EnsureSiteFiles(log, true);
        }

        /// <summary>
        /// 同步升级包附带的站点资源：后端升级目录（CurrentMpTempDir\moviepilot-update\resources）
        /// 中存在 *.pyd / *.bin 文件（有其一或两者皆有）时，移动到当前运行版本的站点资源目录
        /// （CurrentSiteDir），供后端重启后加载（移动即用掉即清，启动 / 升级两处调用共用本方法）。
        /// </summary>
        /// <param name="log">日志回调（后台线程调用，调用方需自行封送）</param>
        public static void SyncSiteResourcesFromUpdate(Action<string> log)
        {
            string resourcesDir = Path.Combine(AppConfig.CurrentMpTempDir, "moviepilot-update", "resources");
            if (!Directory.Exists(resourcesDir))
            {
                return; // 升级包未附带站点资源目录
            }
            try
            {
                bool moved = false;
                foreach (string pattern in new[] { "*.pyd", "*.bin" })
                {
                    foreach (string src in Directory.GetFiles(resourcesDir, pattern))
                    {
                        string dest = Path.Combine(AppConfig.CurrentSiteDir, Path.GetFileName(src));
                        // File.Move 在 .NET Framework 上不支持覆盖，先删旧文件再移动
                        if (File.Exists(dest))
                        {
                            File.Delete(dest);
                        }
                        File.Move(src, dest);
                        log.Info("已移动升级包站点资源: " + Path.GetFileName(src));
                        moved = true;
                    }
                }
                if (!moved)
                {
                    log.Warn("升级包 resources 目录未发现 *.pyd / *.bin 文件，跳过站点资源同步");
                    return;
                }
                // 资源已全部移走：清理空的 resources 目录，下次启动不再重复扫描
                try
                {
                    if (Directory.GetFileSystemEntries(resourcesDir).Length == 0)
                    {
                        Directory.Delete(resourcesDir);
                    }
                }
                catch
                {
                    // 目录非空或被占用时保留，不影响移动结果
                }
            }
            catch (Exception ex)
            {
                log.Error("同步升级包站点资源失败: " + ex.Message);
            }
        }

        /// <summary>强制刷新前备份现有文件；返回 false 表示有旧文件但备份失败（应中止更新以保留原文件）。</summary>
        private static bool TryBackupSiteFile(string file, bool force, out string backup, Action<string> log)
        {
            backup = null;
            if (!force || !File.Exists(file))
            {
                return true; // 非强制模式或无旧文件：无需备份
            }
            string bak = file + ".bak";
            try
            {
                File.Copy(file, bak, true);
                backup = bak;
                return true;
            }
            catch (Exception ex)
            {
                log.Error("备份站点资源失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>下载失败或校验不通过时恢复备份的旧文件。</summary>
        private static void RestoreSiteFile(string backup, string file, Action<string> log)
        {
            if (backup == null || !File.Exists(backup))
            {
                return;
            }
            try
            {
                File.Copy(backup, file, true);
                TryDelete(backup);
            }
            catch (Exception ex)
            {
                log.Error("恢复站点资源失败: " + ex.Message);
            }
        }

        // ==================== 工具方法 ====================

        /// <summary>构建当前生效的代理 URL；未配置或关闭时返回 null（HTTP 下载 / git / pip / Python 环境变量共用）。
        /// 手动 http 代理直接拼 URL；"系统代理"类型读取 Windows Internet 设置（见 ReadSystemProxyUrl），
        /// 仅配置 socks 代理时 HTTP 客户端无法使用，返回 null。targetUrl 供系统代理按目标协议
        /// （http/https）与 PAC 脚本解析使用；无目标（git 全局代理 / 后端环境变量注入）时 PAC 场景
        /// 以 GitHub 为探测目标解析固定代理，静态代理不受影响。</summary>
        public static string BuildProxyUrl(string targetUrl = null)
        {
            string type = (AppSettings.Current.ProxyType ?? "").Trim().ToLowerInvariant();
            string host = (AppSettings.Current.ProxyHost ?? "").Trim();
            int port = AppSettings.Current.ProxyPort;
            if (type == "http" && host.Length > 0 && port > 0)
            {
                return "http://" + host + ":" + port;
            }
            if (type == "system")
            {
                return ReadSystemProxyUrl(targetUrl);
            }
            return null;
        }

        /// <summary>读取 Windows 系统代理（HKCU\...\Internet Settings）：
        /// ① ProxyEnable=1 时解析 ProxyServer 静态代理（兼容 "host:port" 与 "http=...;https=..."
        ///    多协议格式，按目标协议取对应条目，无目标时优先 https）；
        /// ② 静态代理未启用但配置了 PAC 脚本（AutoConfigURL）时按 PAC 解析：有 targetUrl 按实际
        ///    目标解析；无 targetUrl（git 等只能注入固定代理的场景）以 GitHub 为探测目标解析。
        /// 仅 socks 条目 / 纯 socks PAC 结果时返回 null（HTTP 客户端与 git 均无法使用）。</summary>
        private static string ReadSystemProxyUrl(string targetUrl)
        {
            string proxyServer = null;
            bool hasPac = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key == null)
                    {
                        return null;
                    }
                    object autoUrl = key.GetValue("AutoConfigURL");
                    hasPac = autoUrl is string && ((string)autoUrl).Trim().Length > 0;
                    // 仅 ProxyEnable=1 时静态代理生效：代理软件退出时会把 ProxyEnable 置 0（ProxyServer 值残留），
                    // 不检查该标志会误用已失效的代理地址导致连接全部失败
                    int proxyEnable = 0;
                    try { proxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable")); } catch { }
                    object server = key.GetValue("ProxyServer");
                    if (proxyEnable != 0 && server is string)
                    {
                        proxyServer = ((string)server).Trim();
                    }
                }
            }
            catch
            {
                return null;
            }
            if (!string.IsNullOrEmpty(proxyServer))
            {
                return ParseSystemProxyServer(proxyServer, targetUrl);
            }
            if (hasPac)
            {
                // PAC 按目标 URL 返回代理；git 全局 http.proxy / 环境变量注入只能设一个固定地址，
                // 无目标 URL 可带时以 GitHub 为探测目标解析（面板 git 与资源下载主要指向 GitHub）；
                // PAC 对探测目标返回直连时 ResolvePacProxyUrl 返回 null，git 等保持直连，与 PAC 行为一致
                string probeUrl = string.IsNullOrEmpty(targetUrl) ? PacProbeUrl : targetUrl;
                return ResolvePacProxyUrl(probeUrl);
            }
            return null;
        }

        /// <summary>解析 ProxyServer 值：不含 '=' 时整体视为 host:port；含 '=' 时按 ';' 分段取
        /// http/https 条目（v2rayN / Clash 等代理软件写 "http=host:port;https=host:port"），
        /// 目标协议匹配的条目优先，未匹配到时回退任一 http/https 条目；只有 socks 条目时返回 null。</summary>
        private static string ParseSystemProxyServer(string proxyServer, string targetUrl)
        {
            if (proxyServer.IndexOf('=') < 0)
            {
                return "http://" + proxyServer;
            }
            bool preferHttps = !string.IsNullOrEmpty(targetUrl) &&
                               targetUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
            string fallback = null;
            foreach (string part in proxyServer.Split(';'))
            {
                string seg = part.Trim();
                int eq = seg.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string proto = seg.Substring(0, eq).Trim().ToLowerInvariant();
                string value = seg.Substring(eq + 1).Trim();
                if ((proto != "http" && proto != "https") || value.Length == 0)
                {
                    continue;
                }
                if (fallback == null)
                {
                    fallback = value;
                }
                if ((preferHttps && proto == "https") || (!preferHttps && proto == "http"))
                {
                    return "http://" + value;
                }
            }
            return fallback == null ? null : "http://" + fallback;
        }

        /// <summary>PAC 场景：经 WebRequest.GetSystemWebProxy（WinINet 按 PAC 脚本解析）返回目标 URL
        /// 的代理地址；目标直连（PAC 不放行）或解析失败时返回 null。仅静态代理未启用时调用。
        /// PAC 结果只接受 http(s) 代理地址：返回 SOCKS 条目时 .NET 请求与 git http.proxy 均不可用，按 null 处理。</summary>
        private static string ResolvePacProxyUrl(string targetUrl)
        {
            try
            {
                Uri uri;
                if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out uri))
                {
                    return null;
                }
                Uri proxy = WebRequest.GetSystemWebProxy().GetProxy(uri);
                // 未代理该目标时 GetProxy 返回原地址本身（个别实现返回 null）
                if (proxy == null ||
                    string.Equals(proxy.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                // PAC 可能只放行 SOCKS（socks5://...）：HTTP 客户端与 git http.proxy 均不支持，丢弃
                string proxyUrl = proxy.AbsoluteUri;
                if (!proxyUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !proxyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return proxyUrl;
            }
            catch
            {
                return null;
            }
        }

        // ---- 程序集 HTTP 下载器（替代系统 curl） ----
        // HttpWebRequest 是 .NET Framework 内置（无需外部组件，任意 Windows 版本可用）；
        // 代理经 BuildProxyUrl 注入请求；携带 GitHub Token（withAuth）与 UA 头（GitHub API 拒绝无 UA 请求）。

        /// <summary>HTTP 非 2xx 状态错误（携带状态码：4xx 客户端错误确定性失败不重试，
        /// 5xx / 0 属瞬时性错误，由下载方法自动重试）。</summary>
        private sealed class HttpStatusException : Exception
        {
            public int StatusCode { get; private set; }

            public HttpStatusException(int statusCode, string message)
                : base(message)
            {
                StatusCode = statusCode;
            }
        }

        /// <summary>下载 URL 到文件：成功返回 true；失败写日志并删除残留返回 false。
        /// 网络类失败自动重试 3 次（间隔 2 秒），HTTP 4xx 等确定性错误不重试（对齐原 curl --retry 3）。
        /// timeoutSec 为单次请求超时（连接 + 单块传输读超时，默认 5 分钟）。
        /// 跨类复用：PanelUpdateService 下载面板更新 exe 共用本方法。</summary>
        public static bool HttpDownloadToFile(string url, string destFile, Action<string> log, bool withAuth, int timeoutSec = 300)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    log.Info("下载: " + url + (attempt > 0 ? "（第 " + (attempt + 1) + " 次重试）" : ""));
                    using (HttpWebResponse response = SendHttpRequest(url, withAuth, timeoutSec))
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        // 流式落盘：不整包载入内存（git 45MB / python 60MB），避免大文件内存峰值
                        byte[] buffer = new byte[64 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                        }
                    }
                    return true;
                }
                catch (HttpStatusException hse)
                {
                    if (hse.StatusCode >= 400 && hse.StatusCode < 500)
                    {
                        // 4xx（404/403 等）为确定性失败：不重试，直接结束
                        log.Error("下载失败: " + hse.Message);
                        TryDelete(destFile);
                        return false;
                    }
                    if (attempt < 2)
                    {
                        log.Warn("下载失败: " + hse.Message + "，稍后重试");
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        log.Error("下载失败: " + hse.Message);
                        TryDelete(destFile);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt < 2)
                    {
                        log.Warn("下载失败: " + ex.Message + "，稍后重试");
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        log.Error("下载失败: " + ex.Message);
                        TryDelete(destFile);
                        return false;
                    }
                }
            }
            return false;
        }

        /// <summary>GET 请求返回响应文本（UTF-8 解码）；失败写日志并返回 null。
        /// 供查询 GitHub API（面板新版本 tag）等轻量请求使用，超时默认 30 秒。</summary>
        public static string HttpGetText(string url, Action<string> log, bool withAuth, int timeoutSec = 30)
        {
            try
            {
                using (HttpWebResponse response = SendHttpRequest(url, withAuth, timeoutSec))
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                log.Warn("请求失败: " + DescribeHttpError(ex));
                return null;
            }
        }

        /// <summary>下载组件压缩包到 tmp（内部安装流程用；网络类失败已自动重试）。</summary>
        private static bool DownloadFile(string url, string destFile, Action<string> log, bool withAuth)
        {
            return HttpDownloadToFile(url, destFile, log, withAuth, 600);
        }

        /// <summary>发起 GET 并手动跟随重定向，返回最终 2xx 响应（调用方负责释放）。
        /// AllowAutoRedirect=false：.NET 自动重定向在跨主机时仍携带 Authorization，与 curl 语义
        /// 不一致（Token 不应泄露给重定向目标）；逐跳手动跟随，跨主机（如 GitHub 302 →
        /// objects.githubusercontent.com）后自动剥离 Authorization 头。</summary>
        private static HttpWebResponse SendHttpRequest(string url, bool withAuth, int timeoutSec)
        {
            Uri original;
            if (!Uri.TryCreate(url, UriKind.Absolute, out original))
            {
                throw new HttpStatusException(0, "无效的下载地址: " + url);
            }
            string currentUrl = url;
            for (int hop = 0; hop <= 6; hop++)
            {
                // 跨主机跳转后不再携带 GitHub Token（与 curl 跟随重定向时的行为一致）
                bool sendAuth = withAuth &&
                    new Uri(currentUrl).Host.Equals(original.Host, StringComparison.OrdinalIgnoreCase);
                HttpWebRequest request = BuildHttpRequest(currentUrl, sendAuth, timeoutSec);
                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException wex)
                {
                    // 4xx/5xx 由 GetResponse 抛 WebException：提取状态码转 HttpStatusException（模拟 curl --fail）
                    HttpWebResponse errorResp = wex.Response as HttpWebResponse;
                    if (errorResp != null)
                    {
                        int status = (int)errorResp.StatusCode;
                        string desc = string.Format("HTTP {0} {1}", status, errorResp.StatusDescription);
                        errorResp.Dispose();
                        throw new HttpStatusException(status, desc);
                    }
                    throw;
                }
                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string location = response.Headers[HttpResponseHeader.Location];
                    response.Dispose();
                    if (string.IsNullOrEmpty(location))
                    {
                        throw new HttpStatusException(code, "HTTP " + code + " 重定向缺少 Location 头");
                    }
                    // Location 可为相对路径：基于当前 URL 解析为绝对地址再继续跟随
                    currentUrl = new Uri(new Uri(currentUrl), location).AbsoluteUri;
                    continue;
                }
                if (code >= 400)
                {
                    response.Dispose();
                    throw new HttpStatusException(code, "HTTP " + code);
                }
                return response;
            }
            throw new HttpStatusException(0, "重定向次数过多（超过 6 跳），已中止");
        }

        /// <summary>构造 HttpWebRequest：启用 TLS1.2（老系统默认协议不含 TLS1.2，GitHub 已禁用 TLS1.0/1.1）、
        /// 按配置注入代理（关闭或无代理时显式直连，避免 .NET 默认尝试 IE 系统代理与“关闭代理”冲突）、
        /// UA 标识面板名称与版本、携带 GitHub Token（withAuth 且配置了 Token 时）。</summary>
        private static HttpWebRequest BuildHttpRequest(string url, bool withAuth, int timeoutSec)
        {
            // 按位或保留既有协议位（兼容还跑着 TLS1.0 的老内网），保证 TLS1.2 可用即可
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.AllowAutoRedirect = false; // 重定向由 SendHttpRequest 手动跟随（跨主机剥离 Token）
            request.Timeout = timeoutSec * 1000;
            request.ReadWriteTimeout = timeoutSec * 1000;
            request.UserAgent = AppConfig.APP_NAME + "/" + AppConfig.APP_VERSION;
            request.Accept = "*/*";
            if (withAuth)
            {
                string token = (AppSettings.Current.GitHubToken ?? "").Trim();
                if (token.Length > 0)
                {
                    request.Headers["Authorization"] = "Bearer " + token;
                }
            }
            string proxyUrl = BuildProxyUrl(url);
            if (proxyUrl == null)
            {
                // 未配置 / 关闭代理：显式直连（HttpWebRequest 默认会用 IE 系统代理，违背“关闭代理”）
                request.Proxy = null;
            }
            else
            {
                request.Proxy = new WebProxy(proxyUrl);
            }
            return request;
        }

        /// <summary>把下载 / 请求异常转成可读信息：HttpStatusException 直接用其消息
        /// （已含 HTTP 状态码与说明），其余异常取 Message。</summary>
        private static string DescribeHttpError(Exception ex)
        {
            HttpStatusException hse = ex as HttpStatusException;
            return hse != null ? hse.Message : ex.Message;
        }

        // ==================== tar（bsdtar）组件 ====================
        // 解压 python 等 .tar.gz 需要 bsdtar（libarchive 编译）：Windows 10 1803 / Server 2019 起系统内置
        // System32\tar.exe 即 libarchive（bsdtar）编译；更旧系统缺失时自动下载 libarchive 发行包部署到
        // runtime\tar\tar.exe（dynamic 包内 DLL 依赖同目录，需整体复制）。部署目录已加入 BuildEnvPath，
        // 面板启动的 git / uv / pip 子进程及其孙进程都能找到；tar 命令一律以解析出的全路径直接执行。

        private static string tarExePath;   // 会话内已解析的可用 bsdtar 全路径（null = 未解析或解析失败）
        private static bool tarUnavailable; // 本轮环境准备已确认无 bsdtar 可用（每轮开始重置，见 EnsureEnvironment）

        /// <summary>确保 bsdtar 可用（环境准备阶段调用一次，解压前经 ResolveTarExe 复用缓存路径）：
        /// 先查便携版 runtime\tar\tar.exe 与系统 System32\tar.exe（须为 bsdtar 编译），
        /// 均不可用时自动下载 libarchive 并部署便携版；部署也失败则记录 tarUnavailable，本次运行不再重试。</summary>
        public static void EnsureTar(Action<string> log)
        {
            if (tarExePath != null || tarUnavailable)
            {
                return;
            }
            string candidate = FindUsableTar(log);
            if (candidate == null)
            {
                candidate = DeployPortableTar(log);
            }
            if (candidate != null)
            {
                tarExePath = candidate;
            }
            else
            {
                tarUnavailable = true;
                log.Error("未找到可用的 bsdtar，.tar.gz 解压将不可用（请检查网络后重试启动服务）");
            }
        }

        /// <summary>返回可用 bsdtar 全路径（供 ExtractArchive 解压 tar.gz）；不可用返回 null。</summary>
        private static string ResolveTarExe(Action<string> log)
        {
            if (tarExePath == null && !tarUnavailable)
            {
                EnsureTar(log);
            }
            return tarExePath;
        }

        /// <summary>按优先级查找已部署的 bsdtar：便携版 runtime\tar\tar.exe（面板自部署、版本可控）
        /// → 系统 System32\tar.exe。找到的文件须通过 IsBsdTar 校验（GNU tar 不具备 bsdtar 的 zip 解压
        /// 与 Windows 路径兼容性，系统目录被 GNU tar 覆盖时须忽略并自动部署便携版替代）。</summary>
        private static string FindUsableTar(Action<string> log)
        {
            foreach (string dir in new[] { AppConfig.TAR_DIR, Environment.SystemDirectory })
            {
                string candidate = Path.Combine(dir, "tar.exe");
                if (!File.Exists(candidate))
                {
                    continue;
                }
                if (IsBsdTar(candidate))
                {
                    return candidate;
                }
                log.Warn("发现 " + candidate + " 但不是 bsdtar（libarchive）编译，自动部署便携版替代");
            }
            return null;
        }

        /// <summary>校验是否为 bsdtar：执行 tar --version，输出含 "bsdtar" 即 libarchive 编译
        /// （Windows 系统内置 tar.exe 即此实现）；GNU tar（git 附带 / WSL）输出不含该标识。</summary>
        private static bool IsBsdTar(string tarExe)
        {
            string output = RunProcessOutput(tarExe, "--version", AppConfig.TMP_DIR, null);
            return output != null && output.IndexOf("bsdtar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>下载 libarchive 发行包（zip，由程序集解压）并部署便携版 bsdtar：
        /// dynamic 包 bin 目录为 bsdtar.exe 与其运行依赖 DLL（同目录加载，须整体复制），
        /// bsdtar.exe 重命名为 tar.exe 后重新校验；返回部署后的 tar.exe 路径，失败返回 null。</summary>
        private static string DeployPortableTar(Action<string> log)
        {
            log.Warn("未找到可用的 bsdtar，开始下载便携版 tar（libarchive " + TarVersion + "）...");
            string archive = Path.Combine(AppConfig.TMP_DIR, "libarchive-" + TarVersion + ".zip");
            string extractDir = Path.Combine(AppConfig.TMP_DIR, "tar-extract-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!DownloadFile(TarDownloadUrl, archive, log, false))
                {
                    return null;
                }
                if (!ExtractArchive(archive, extractDir, log))
                {
                    return null; // zip 走程序集解压，此时不依赖 tar，无递归风险
                }
                string binDir = Path.Combine(extractDir, "bin");
                if (!Directory.Exists(binDir) || !File.Exists(Path.Combine(binDir, "bsdtar.exe")))
                {
                    log.Error("libarchive 压缩包结构异常（缺少 bin\\bsdtar.exe），部署中止");
                    return null;
                }
                Directory.CreateDirectory(AppConfig.TAR_DIR);
                MoveContents(binDir, AppConfig.TAR_DIR);
                // bsdtar.exe → tar.exe：代码与子进程 PATH（BuildEnvPath）统一按 tar.exe 引用
                string bsdtarFile = Path.Combine(AppConfig.TAR_DIR, "bsdtar.exe");
                string tarFile = Path.Combine(AppConfig.TAR_DIR, "tar.exe");
                if (File.Exists(bsdtarFile))
                {
                    if (File.Exists(tarFile))
                    {
                        File.Delete(tarFile);
                    }
                    File.Move(bsdtarFile, tarFile);
                }
                if (!IsBsdTar(tarFile))
                {
                    log.Error("便携版 tar 校验失败（非 bsdtar），部署中止: " + tarFile);
                    return null;
                }
                log.Info("便携版 bsdtar 部署完成: " + tarFile);
                return tarFile;
            }
            catch (Exception ex)
            {
                log.Error("部署便携版 tar 失败: " + ex.Message);
                return null;
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        /// <summary>解压压缩包到目标目录（目标自动创建），成功返回 true：
        /// zip 由 .NET 程序集直接解压（System.IO.Compression，任意 Windows 版本可用，不再依赖 tar）；
        /// tar.gz / tgz 用 bsdtar 解压——tar 全路径执行（不依赖 PATH 查找），
        /// 便携版 / 系统 tar 缺失时由 EnsureTar 自动下载部署。</summary>
        private static bool ExtractArchive(string archive, string destDir, Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(destDir);
            }
            catch (Exception ex)
            {
                log.Error("创建解压目录失败: " + ex.Message);
                return false;
            }
            string name = (archive ?? "").ToLowerInvariant();
            try
            {
                if (name.EndsWith(".zip"))
                {
                    ZipFile.ExtractToDirectory(archive, destDir);
                    return true;
                }
                if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz"))
                {
                    string tarExe = ResolveTarExe(log);
                    if (tarExe == null)
                    {
                        log.Error("未找到可用的 bsdtar（自动部署失败），无法解压: " + Path.GetFileName(archive));
                        return false;
                    }
                    int code = RunProcess(tarExe, "-xf \"" + archive + "\" -C \"" + destDir + "\"", destDir, null);
                    if (code != 0)
                    {
                        log.Error("解压失败（tar 退出码 " + code + "）: " + Path.GetFileName(archive));
                        return false;
                    }
                    return true;
                }
                log.Warn("不支持的压缩包格式，跳过解压: " + Path.GetFileName(archive));
                return false;
            }
            catch (Exception ex)
            {
                log.Error("解压失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>把源目录的全部内容移动到目标目录（目标自动创建）。
        /// 目标存在同名条目时覆盖式合并：目录递归合并子内容（保留目录内既有其他文件），
        /// 文件先删旧再移动 —— .NET Framework 的 Directory.Move / File.Move 不允许目标已存在，
        /// 而安装目录可能先有残留内容（如 Nginx 主体未装成时 SyncNginxConfigs 已把配置同步进
        /// conf、或上次安装中断留下的半成品），直接移动会抛 IOException 导致组件永远装不上。</summary>
        private static void MoveContents(string srcDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string entry in Directory.GetFileSystemEntries(srcDir))
            {
                string target = Path.Combine(destDir, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(target))
                    {
                        // 同名目录已存在：递归合并子内容（覆盖同名文件、保留目标内既有其他文件）
                        MoveContents(entry, target);
                        try { Directory.Delete(entry); } catch { }
                    }
                    else
                    {
                        Directory.Move(entry, target);
                    }
                }
                else
                {
                    // File.Move 在 .NET Framework 上不支持覆盖：先删除旧文件再移动
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    File.Move(entry, target);
                }
            }
        }

        /// <summary>解析 version.py 中 FRONTEND_VERSION = 'vX.Y.Z(-suffix)' 的版本号；不存在或格式不符时返回 null。</summary>
        private static string ReadFrontendVersion(string versionPy)
        {
            try
            {
                if (!File.Exists(versionPy))
                {
                    return null;
                }
                Match m = Regex.Match(File.ReadAllText(versionPy), @"FRONTEND_VERSION\s*=\s*['""]([\w.\-]+)['""]");
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>读取纯文本版本文件并 Trim；不存在或为空时返回 null。</summary>
        private static string ReadVersionFile(string file)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return null;
                }
                string v = File.ReadAllText(file).Trim();
                return v.Length > 0 ? v : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 比较两个前端版本号（形如 v3.0.1、v3.0.1-1、v3.0.1-beta01），返回 a 相对 b 的大小（>0 表示 a 更新）。
        /// 先比三段数字；数字相同时：纯数字后缀（补丁发布，如 -1）> 无后缀 > 预发布后缀（beta/alpha/rc/dev）。
        /// </summary>
        private static int CompareFrontendVersions(string a, string b)
        {
            string numA, sufA, numB, sufB;
            SplitVersion(a, out numA, out sufA);
            SplitVersion(b, out numB, out sufB);
            int[] partsA = ParseNumericParts(numA);
            int[] partsB = ParseNumericParts(numB);
            for (int i = 0; i < 3; i++)
            {
                if (partsA[i] != partsB[i])
                {
                    return partsA[i].CompareTo(partsB[i]);
                }
            }
            return CompareSuffix(sufA, sufB);
        }

        /// <summary>拆分为数字部分与后缀（- 之后的部分，无则空串），忽略大小写 v 前缀。</summary>
        private static void SplitVersion(string version, out string numeric, out string suffix)
        {
            numeric = "";
            suffix = "";
            if (string.IsNullOrEmpty(version))
            {
                return;
            }
            string v = version.Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            numeric = dash < 0 ? v : v.Substring(0, dash);
            if (dash >= 0)
            {
                suffix = v.Substring(dash + 1);
            }
        }

        /// <summary>解析三段数字（不足补 0，非数字记 0）。</summary>
        private static int[] ParseNumericParts(string numeric)
        {
            int[] parts = new int[3];
            string[] seg = numeric.Split('.');
            for (int i = 0; i < 3 && i < seg.Length; i++)
            {
                int v;
                if (!int.TryParse(seg[i], out v))
                {
                    v = 0;
                }
                parts[i] = v;
            }
            return parts;
        }

        /// <summary>数字段相同时比较后缀：纯数字（补丁发布）> 无后缀 > 预发布（beta/alpha/rc/dev）。</summary>
        private static int CompareSuffix(string a, string b)
        {
            int rankA = SuffixRank(a);
            int rankB = SuffixRank(b);
            if (rankA != rankB)
            {
                return rankA.CompareTo(rankB);
            }
            if (rankA == 2)
            {
                long na, nb;
                long.TryParse(a, out na);
                long.TryParse(b, out nb);
                return na.CompareTo(nb);
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>后缀优先级：纯数字（补丁发布）2 > 无后缀 1 > 预发布（beta/alpha/rc/dev）0。</summary>
        private static int SuffixRank(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return 1;
            }
            long num;
            if (long.TryParse(suffix.Trim(), out num))
            {
                return 2;
            }
            string lower = suffix.Trim().ToLowerInvariant();
            if (lower.StartsWith("beta") || lower.StartsWith("alpha") ||
                lower.StartsWith("rc") || lower.StartsWith("dev"))
            {
                return 0;
            }
            return 1; // 其他未知后缀与无后缀同级
        }

        /// <summary>pyd 是 PE 文件：校验 DOS 头 MZ 与最小体积。</summary>
        private static bool IsValidPyd(string file)
        {
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length < 1024)
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

        // ---- 面板活动子进程管理 ----
        // 解压 / 命令子进程（tar / git / pip / uv 等；HTTP 下载已程序集化，不产生子进程）注册于此：
        // 面板退出时由 KillActiveProcesses 统一终止，防止长任务在面板退出后遗留运行
        // （Windows 子进程不随父进程退出自动结束）。

        private static readonly ConcurrentDictionary<int, Process> ActiveProcesses = new ConcurrentDictionary<int, Process>();

        /// <summary>注册面板启动的子进程（短任务，如 tar / git / pip / uv）。</summary>
        public static void TrackProcess(Process p)
        {
            try { ActiveProcesses[p.Id] = p; } catch { }
        }

        /// <summary>注销已结束的子进程（WaitForExit 返回后调用，避免表内残留已释放对象）。</summary>
        public static void UntrackProcess(Process p)
        {
            Process removed;
            try { ActiveProcesses.TryRemove(p.Id, out removed); } catch { }
        }

        // TODO: 面板迁移到 .NET Core 3+ 后删除本方法
        /// <summary>强制回收子进程结束后未确定性释放的内核句柄（调用点须已 Dispose 进程对象并清除其引用）。
        /// 背景：.NET Framework 的 Process.Dispose 只释放进程句柄，不关闭重定向标准流（3 个匿名管道 File
        /// 句柄）与 BeginOutputReadLine 异步读内部的等待事件（Event 句柄），二者只随终结器在 GC 时回收；
        /// 面板空闲时分配速率低（KB/s 级），自然 GC 间隔达数十分钟~小时级，期间每次子进程调用净增约
        /// 5 个句柄（点击“启动服务”触发 git config 即 +3 File +2 Event，实测），持续累积到下次自然 GC 才回落。
        /// 本方法在子进程结束事件处立即触发一次终结器执行，等效 .NET Core 3.0 起 Process.Dispose 主动
        /// 关流的确定性释放语义（该修复不回溯 .NET Framework）；调用点均属低频事件（按钮点击 / 更新步骤），
        /// 毫秒级开销可忽略。若将来面板迁移到 .NET Core 3+，删除各调用点的本调用即可。</summary>
        public static void ReclaimProcessHandles()
        {
            // 三段式：先收集（带终结器的对象进入终结队列）→ 等终结器执行完（释放内核句柄）→ 再收集彻底回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>终止所有面板启动且仍在运行的子进程（Form1 退出放行前调用）。</summary>
        public static void KillActiveProcesses()
        {
            foreach (int pid in ActiveProcesses.Keys)
            {
                Process p;
                if (!ActiveProcesses.TryRemove(pid, out p)) continue;
                try
                {
                    if (!p.HasExited) p.Kill();
                }
                catch { }
                try { p.Dispose(); } catch { }
            }
        }

        /// <summary>执行进程，等待退出；超时（5 分钟）则强制结束并返回 -1。</summary>
        private static int RunProcess(string fileName, string arguments, string workingDir, string envPath, Dictionary<string, string> extraEnv = null)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir ?? AppConfig.TMP_DIR,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // stdin 必须重定向为有效管道：面板是 GUI 进程（无控制台，标准句柄无效），子进程
                    // （uv / git）再启动孙进程（uv 构建源码包的临时 python、git 的 ssh/hook 等）时
                    // 继承无效句柄会让 CreateProcess 报“句柄无效 (os error 6)”（如 uv 构建 brotli）
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // 子进程（tar / git / pip / uv）输出统一按 UTF-8 解码：git 与 uv（Rust）输出 UTF-8，
                    // pip 经 PYTHONUTF8=1 强制 UTF-8；按系统 ANSI 代码页（GBK）解码中文会乱码
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (envPath != null)
                {
                    psi.EnvironmentVariables["PATH"] = envPath + ";" + Environment.GetEnvironmentVariable("PATH");
                }
                if (extraEnv != null)
                {
                    foreach (var kv in extraEnv)
                    {
                        psi.EnvironmentVariables[kv.Key] = kv.Value;
                    }
                }
                Process p = Process.Start(psi);
                // 注册到活动进程表：面板退出时统一终止，防止下载等长任务遗留
                TrackProcess(p);
                try
                {
                    // 异步读取双管道，避免串行 ReadToEnd 导致 stderr 缓冲满（长输出命令）死锁；
                    // 子进程输出统一按 DEBUG 级别转发到面板日志（仅配置“打印Debug日志”时显示）：
                    // 解压 / 依赖安装等耗时命令执行期间勾选 Debug 时即可看到进度
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) Form1.Debug(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) Form1.Debug(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    // 大文件下载（git 45MB / python 60MB）与 pip 安装耗时较长，超时放宽到 20 分钟
                    if (!p.WaitForExit(20 * 60 * 1000))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    return p.ExitCode;
                }
                finally
                {
                    // 释放进程对象并抹除本地引用后立即回收重定向管道 / 异步读句柄：
                    // 置 null 防 Debug 构建下 JIT 延长局部变量生存期导致回收不彻底，原因详见 ReclaimProcessHandles
                    UntrackProcess(p);
                    try { p.Dispose(); } catch { }
                    p = null;
                    ReclaimProcessHandles();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("执行进程失败 " + fileName + ": " + ex.Message);
                return -1;
            }
        }

        /// <summary>执行进程并返回完整输出（用于 pip 等需要查看输出的场景）。</summary>
        private static string RunProcessOutput(string fileName, string arguments, string workingDir, string envPath, Dictionary<string, string> extraEnv = null)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir ?? AppConfig.TMP_DIR,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // stdin 必须重定向为有效管道：面板是 GUI 进程（无控制台，标准句柄无效），子进程
                    // （uv / git）再启动孙进程（uv 构建源码包的临时 python、git 的 ssh/hook 等）时
                    // 继承无效句柄会让 CreateProcess 报“句柄无效 (os error 6)”（如 uv 构建 brotli）
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // 子进程（tar / git / pip / uv）输出统一按 UTF-8 解码：git 与 uv（Rust）输出 UTF-8，
                    // pip 经 PYTHONUTF8=1 强制 UTF-8；按系统 ANSI 代码页（GBK）解码中文会乱码
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (envPath != null)
                {
                    psi.EnvironmentVariables["PATH"] = envPath + ";" + Environment.GetEnvironmentVariable("PATH");
                }
                if (extraEnv != null)
                {
                    foreach (var kv in extraEnv)
                    {
                        psi.EnvironmentVariables[kv.Key] = kv.Value;
                    }
                }
                StringBuilder sb = new StringBuilder();
                Process p = Process.Start(psi);
                // 注册到活动进程表：面板退出时统一终止，防止下载等长任务遗留
                TrackProcess(p);
                try
                {
                    // 收集完整输出供调用方判断，同时逐行按 DEBUG 级别转发到面板日志
                    // （仅配置“打印Debug日志”时显示）：uv sync / pip install 执行期间
                    // 勾选 Debug 时即可看到进度，不再等命令结束才一次性倒出
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) return; sb.AppendLine(e.Data); Form1.Debug(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; sb.AppendLine(e.Data); Form1.Debug(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    // uv 全量安装依赖可能超过 5 分钟，超时放宽到 10 分钟
                    if (!p.WaitForExit(10 * 60 * 1000))
                    {
                        try { p.Kill(); } catch { }
                        p.WaitForExit();
                        return sb.ToString();
                    }
                    p.WaitForExit();
                    return sb.ToString();
                }
                finally
                {
                    // 释放进程对象并抹除本地引用后立即回收重定向管道 / 异步读句柄：
                    // 置 null 防 Debug 构建下 JIT 延长局部变量生存期导致回收不彻底，原因详见 ReclaimProcessHandles
                    UntrackProcess(p);
                    try { p.Dispose(); } catch { }
                    p = null;
                    ReclaimProcessHandles();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("执行进程失败 " + fileName + ": " + ex.Message);
                return "";
            }
        }

        private static void TryDelete(string file)
        {
            // 进程被 Kill 后文件锁可能延迟释放，重试 3 次
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (file != null && File.Exists(file)) File.Delete(file);
                    return;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        }

        private static void Cleanup(string archive, string extractDir)
        {
            TryDelete(archive);
            try
            {
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            }
            catch
            {
            }
        }
    }
}
