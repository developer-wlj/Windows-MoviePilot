using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// 首次运行环境准备：下载便携版 Nginx / Git / Python 3.14.7、同步 nginx 配置到安装目录、
    /// 建立 Python 虚拟环境（后端运行在 venv 中）、下载站点资源（GitHub raw，支持 Token）。
    /// 所有下载支持代理（http / socks5）；代理为空或关闭时清空 git 全局代理。
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

        /// <summary>完整初始化流程：代理 → nginx → git → python → 配置同步 → 代码（clone+补丁）→ venv（依赖）→ 站点资源。</summary>
        public static void EnsureEnvironment(Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.TMP_DIR); // 下载临时目录（BASE_DIR\tmp）
            }
            catch
            {
            }
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
            log("未找到后端代码，开始克隆官方仓库并打补丁...");
            string error = UpgradeService.EnsureCode(log);
            if (error != null)
            {
                log("错误: " + error);
                log("后端代码缺失，无法启动服务（请检查网络 / 代理 / GitHub Token 后重试）");
            }
            else
            {
                log("后端代码就绪（官方 v3 + v3-rebase 补丁）");
                // 首次克隆后同步前端：按 version.py 的 FRONTEND_VERSION 对比本地版本，更高才下载覆盖
                EnsureFrontend(log);
            }
        }

        /// <summary>
        /// 首次克隆后端代码或升级后，读取 version.py 的 FRONTEND_VERSION，与 mp-web\version.txt
        /// 当前版本对比，后端要求版本更高时用 curl 下载对应版本的前端发行包（dist.zip）到 tmp，
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
                log("警告: 未从 version.py 解析到 FRONTEND_VERSION，跳过前端资源下载");
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
                log("前端本地版本高于要求（本地 " + currentVersion + "，要求 " + frontendVersion + "），跳过下载");
                return;
            }
            if (versionCmp == 0)
            {
                if (!force)
                {
                    log("前端已是最新（本地 " + currentVersion + "，要求 " + frontendVersion + "），跳过下载");
                    return;
                }
                log("前端版本相同（" + currentVersion + "），按强制更新配置重新下载覆盖（官方同版本号可能更新内容）...");
            }
            else
            {
                log("前端版本 " + (currentVersion ?? "未知") + " 低于要求 " + frontendVersion + "，开始下载...");
            }

            string archive = Path.Combine(AppConfig.TMP_DIR, "frontend-dist-" + frontendVersion + ".zip");
            string url = FrontendReleaseBase + frontendVersion + "/dist.zip";
            if (!DownloadFile(url, archive, log, true))
            {
                log("前端资源下载失败（可稍后手动触发立即升级版本重试）");
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
                log("前端压缩包结构异常（缺少 dist/index.html），已跳过覆盖");
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
                log("前端资源已更新到 " + frontendVersion + ": " + AppConfig.FRONTEND_DIR);
            }
            catch (Exception ex)
            {
                log("覆盖前端资源失败: " + ex.Message);
            }
            finally
            {
                Cleanup(archive, extractDir);
            }
        }

        /// <summary>应用或清空 git 全局代理（http/socks5；为空或关闭时清空）。</summary>
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
                log("已设置 git 全局代理: " + proxyUrl);
            }
            else
            {
                // 未设置过时 unset 返回非零，忽略
                RunProcess(gitExe, "config --global --unset-all http.proxy", AppConfig.GIT_DIR, null);
                log("已清空 git 全局代理");
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
            log("未找到Nginx，开始下载...");
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
                    log("Nginx 压缩包结构异常");
                    return;
                }
                MoveContents(src, AppConfig.NGINX_DIR);
                log("Nginx " + NginxVersion + " 安装完成: " + AppConfig.NGINX_DIR);
            }
            catch (Exception ex)
            {
                log("安装 Nginx 失败: " + ex.Message);
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
            log("未找到便携版 Git，开始下载...");
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
                log("Git 便携版安装完成: " + AppConfig.GIT_DIR);
                ApplyGitProxy(log); // git 就绪后应用代理
            }
            catch (Exception ex)
            {
                log("安装 Git 失败: " + ex.Message);
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
            log("未找到便携版 " + pythonDesc + "，开始下载...");
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
                    log("Python 压缩包结构异常");
                    return;
                }
                MoveContents(src, AppConfig.CurrentPythonDir);
                log(pythonDesc + " 便携版安装完成: " + AppConfig.CurrentPythonDir);
            }
            catch (Exception ex)
            {
                log("安装 Python 失败: " + ex.Message);
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
            log("未找到便携版 uv " + UvVersion + "，开始下载...");
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
                log("uv " + UvVersion + " 安装完成: " + AppConfig.UV_DIR);
            }
            catch (Exception ex)
            {
                log("安装 uv 失败: " + ex.Message);
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
                    log("警告: 未找到配置文件 " + src);
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
                        log("已同步 " + name + " 到 " + AppConfig.NGINX_CONFIG_DIR);
                    }
                }
                catch (Exception ex)
                {
                    log("同步 " + name + " 失败: " + ex.Message);
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
                log("Python 未就绪，跳过虚拟环境创建");
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
                    log("虚拟环境与当前 Python 版本不匹配，删除重建: " + AppConfig.CurrentVenvDir);
                    try
                    {
                        Directory.Delete(AppConfig.CurrentVenvDir, true);
                    }
                    catch (Exception ex)
                    {
                        log("删除旧虚拟环境失败（可能被占用）: " + ex.Message);
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
                log("清理无效虚拟环境残留: " + AppConfig.CurrentVenvDir);
                try
                {
                    Directory.Delete(AppConfig.CurrentVenvDir, true);
                }
                catch (Exception ex)
                {
                    log("清理无效虚拟环境失败（可能被占用）: " + ex.Message);
                    return;
                }
            }
            log("创建 Python 虚拟环境: " + AppConfig.CurrentVenvDir);
            // 用 RunProcessOutput 捕获创建输出：失败时日志直接给出 python 的错误原因（而非仅退出码）
            string uvExe = Path.Combine(AppConfig.UV_DIR, "uv.exe");
            if (!File.Exists(uvExe))
            {
                log("未找到便携版 uv，跳过依赖安装");
                return;
            }
            string venvOutput = RunProcessOutput(uvExe, "venv \"" + AppConfig.CurrentVenvDir + "\" -p \"" + AppConfig.GetPythonExe() + "\"", AppConfig.BIN_DIR, AppConfig.BuildEnvPath());
            if (!File.Exists(venvPython))
            {
                string detail = venvOutput.Trim();
                log("创建虚拟环境失败: " + (detail.Length > 0 ? detail : "（无错误输出）"));
                return;
            }
            log("虚拟环境创建完成");
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
                log("安装 Python 依赖（首次可能耗时较长）...");
                // 配置了代理（http/socks5）时 pip 也走同一代理（--proxy），与 git / curl 保持一致
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
                log("未找到 requirements.txt / pyproject.toml，跳过依赖安装");
                return;
            }
            string uvExe = Path.Combine(AppConfig.UV_DIR, "uv.exe");
            if (!File.Exists(uvExe))
            {
                log("未找到便携版 uv，跳过依赖安装");
                return;
            }
            log("安装 Python 依赖（uv sync，首次可能耗时较长）...");
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
                log("创建站点资源目录失败: " + ex.Message);
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
                    log("错误: 备份 " + SitesPydFileName + " 失败，已跳过更新（保留原文件）");
                    ok = false;
                }
                else if (!DownloadFile(SitesPydFileUrl, pydFile, log, true))
                {
                    RestoreSiteFile(pydBackup, pydFile, log);
                    log("错误: 下载 " + SitesPydFileName + " 失败，已保留原文件（请检查网络 / GitHub Token）");
                    ok = false;
                }
                else if (!IsValidPyd(pydFile))
                {
                    RestoreSiteFile(pydBackup, pydFile, log);
                    log("错误: " + SitesPydFileName + " 文件不完整，已恢复原文件");
                    ok = false;
                }
                else
                {
                    TryDelete(pydBackup);
                    log(force ? "认证资源已更新: " + SitesPydFileName : "站点资源下载完成: " + SitesPydFileName);
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
                    log("错误: 备份 user.sites.v3.bin 失败，已跳过更新（保留原文件）");
                    ok = false;
                }
                else if (!DownloadFile(SitesBinUrl, binFile, log, true))
                {
                    RestoreSiteFile(binBackup, binFile, log);
                    log("错误: 下载 user.sites.v3.bin 失败，已保留原文件（请检查网络 / GitHub Token）");
                    ok = false;
                }
                else
                {
                    TryDelete(binBackup);
                    log(force ? "站点资源已更新: user.sites.v3.bin" : "站点资源下载完成: user.sites.v3.bin");
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
                        log("已移动升级包站点资源: " + Path.GetFileName(src));
                        moved = true;
                    }
                }
                if (!moved)
                {
                    log("升级包 resources 目录未发现 *.pyd / *.bin 文件，跳过站点资源同步");
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
                log("同步升级包站点资源失败: " + ex.Message);
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
                log("备份站点资源失败: " + ex.Message);
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
                log("恢复站点资源失败: " + ex.Message);
            }
        }

        // ==================== 工具方法 ====================

        /// <summary>构建代理 URL；未配置或关闭时返回 null（curl / git / pip / Python 环境变量共用）。
        /// socks5 一律按 socks5h 处理：socks5 默认客户端本地解析域名，本地 DNS 被污染时会解析出
        /// 错误 IP 导致连接失败（表现为“不走代理”）；socks5h 由代理服务器解析，与 http 代理行为
        /// 一致，访问 GitHub 等境外站点更可靠。</summary>
        public static string BuildProxyUrl()
        {
            string type = (AppSettings.Current.ProxyType ?? "").Trim().ToLowerInvariant();
            string host = (AppSettings.Current.ProxyHost ?? "").Trim();
            int port = AppSettings.Current.ProxyPort;
            if ((type == "http" || type == "socks5") && host.Length > 0 && port > 0)
            {
                // socks5h 是 socks5 的远程 DNS 变体（握手协议不变，仅域名解析位置不同）
                string scheme = type == "socks5" ? "socks5h" : "http";
                return scheme + "://" + host + ":" + port;
            }
            return null;
        }

        /// <summary>构造 curl 参数：跟随重定向、失败退出、超时与重试，可选代理与 GitHub Token。
        /// --max-time 120：单次请求总时长上限 120 秒（连接 + 传输，与 git 命令超时一致），
        /// 防止慢速传输无限挂住；配合 --retry 3 重试，总时长由 RunProcess 的 20 分钟进程级兜底覆盖。</summary>
        private static string BuildCurlArgs(string url, string outFile, bool withAuth)
        {
            string args = "-L --fail --connect-timeout 15 --max-time 120 --retry 3 --retry-delay 2 -o \"" + outFile + "\" \"" + url + "\"";
            string proxyUrl = BuildProxyUrl();
            if (proxyUrl != null)
            {
                args = "--proxy \"" + proxyUrl + "\" " + args;
            }
            if (withAuth)
            {
                string token = (AppSettings.Current.GitHubToken ?? "").Trim();
                if (token.Length > 0)
                {
                    args = "-H \"Authorization: Bearer " + token + "\" " + args;
                }
            }
            return args;
        }

        /// <summary>下载文件：成功返回 true；失败删除残留文件并返回 false。</summary>
        private static bool DownloadFile(string url, string destFile, Action<string> log, bool withAuth)
        {
            string curlExe = Path.Combine(Environment.SystemDirectory, "curl.exe");
            if (!File.Exists(curlExe))
            {
                log("未找到系统 curl.exe，无法下载");
                return false;
            }
            log("下载: " + url);
            int code = RunProcess(curlExe, BuildCurlArgs(url, destFile, withAuth), AppConfig.TMP_DIR, null);
            if (code != 0)
            {
                log("下载失败（curl 退出码 " + code + "）");
                TryDelete(destFile);
                return false;
            }
            return true;
        }

        /// <summary>用系统 tar.exe（bsdtar）解压 zip / tar.gz 到目标目录。</summary>
        private static bool ExtractArchive(string archive, string destDir, Action<string> log)
        {
            string tarExe = Path.Combine(Environment.SystemDirectory, "tar.exe");
            if (!File.Exists(tarExe))
            {
                log("未找到系统 tar.exe，无法解压");
                return false;
            }
            try
            {
                Directory.CreateDirectory(destDir);
            }
            catch (Exception ex)
            {
                log("创建解压目录失败: " + ex.Message);
                return false;
            }
            int code = RunProcess(tarExe, "-xf \"" + archive + "\" -C \"" + destDir + "\"", destDir, null);
            if (code != 0)
            {
                log("解压失败（tar 退出码 " + code + "）");
                return false;
            }
            return true;
        }

        /// <summary>把源目录的全部内容移动到目标目录（目标自动创建）。</summary>
        private static void MoveContents(string srcDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string entry in Directory.GetFileSystemEntries(srcDir))
            {
                string target = Path.Combine(destDir, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, target);
                }
                else
                {
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
        // 下载/命令子进程（curl / tar / git / pip 等）注册于此：面板退出时由 KillActiveProcesses
        // 统一终止，防止下载等长任务在面板退出后遗留运行（Windows 子进程不随父进程退出自动结束）。

        private static readonly ConcurrentDictionary<int, Process> ActiveProcesses = new ConcurrentDictionary<int, Process>();

        /// <summary>注册面板启动的子进程（短任务，如 curl / tar / git / pip）。</summary>
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
                    // 子进程（curl / git / pip / uv）输出统一按 UTF-8 解码：git 与 uv（Rust）输出 UTF-8，
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
                using (Process p = Process.Start(psi))
                {
                    // 注册到活动进程表：面板退出时统一终止，防止下载等长任务遗留
                    TrackProcess(p);
                    try
                    {
                        // 异步读取双管道，避免串行 ReadToEnd 导致 stderr 缓冲满（curl 进度条）死锁；
                        // 子进程输出统一按 DEBUG 级别转发到面板日志（仅配置“打印Debug日志”时显示）：
                        // 下载 / 解压等耗时命令执行期间勾选 Debug 时即可看到进度
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
                        UntrackProcess(p);
                    }
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
                    // 子进程（curl / git / pip / uv）输出统一按 UTF-8 解码：git 与 uv（Rust）输出 UTF-8，
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
                using (Process p = Process.Start(psi))
                {
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
                    }
                    finally
                    {
                        UntrackProcess(p);
                    }
                }
                return sb.ToString();
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
