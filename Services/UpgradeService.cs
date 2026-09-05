using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// 版本升级流程：git fetch + checkout -B 重建 + cherry-pick 补丁 + uv sync，然后重启服务。
    /// </summary>
    public static class UpgradeService
    {
        // 代码仓库（唯一）：git clone / 升级一律以 jxxghp 官方源为准
        private const string VersionRepo = "https://github.com/jxxghp/MoviePilot.git";
        // 补丁仓库（v3-rebase 分支）：gitee 源优先，wlj 源备用；更新后 cherry-pick 标题含 rebase 的补丁提交
        private static readonly string[] PatchRepos = new string[]
        {
            "https://gitee.com/vueconfig/MoviePilot.git",
            "https://github.com/developer-wlj/MoviePilot.git"
        };
        private const string PatchBranch = "v3-rebase";
        /// <summary>
        /// 确保后端代码存在：目录不是 Git 仓库时，从官方源克隆 v3 分支并立即打 v3-rebase 补丁，
        /// 再把远端同步为官方仓库（幂等，启动/升级路径复用）。
        /// 站点资源与依赖安装都位于代码目录内，必须在虚拟环境 / 站点资源下载之前执行。
        /// 返回错误信息，null 表示成功（代码已就绪）。
        /// </summary>
        public static string EnsureCode(Action<string> log)
        {
            if (!EnsureGitReady(log))
            {
                return "未找到便携版 Git，请先点击\"启动服务\"完成环境准备（自动下载 Git）。";
            }

            string envPath = AppConfig.BuildEnvPath();
            string gitExe = Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe");

            // 后端目录不是 Git 仓库：从官方仓库首次克隆
            if (!Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, ".git")))
            {
                if (Directory.Exists(AppConfig.CurrentBackendDir) &&
                    Directory.GetFileSystemEntries(AppConfig.CurrentBackendDir).Length > 0)
                {
                    // 仅残留站点资源文件（旧版流程在克隆前下载了资源）时先备份到 tmp 再克隆，避免误报
                    string backupDir = Path.Combine(AppConfig.TMP_DIR, "preclone-site-backup");
                    bool moved = false;
                    try
                    {
                        Directory.CreateDirectory(backupDir);
                        // 克隆前清理该目录下残留的站点资源（按当前运行版本的 pyd 文件名；
                        // 站点资源与后端代码一起克隆，避免残留旧版本文件）
                        foreach (string f in new[] { EnvironmentSetup.SitesPydFileName, "user.sites.v3.bin" })
                        {
                            string src = Path.Combine(AppConfig.CurrentSiteDir, f);
                            if (File.Exists(src))
                            {
                                File.Move(src, Path.Combine(backupDir, f));
                                moved = true;
                            }
                        }
                        // 无论是否有文件残留，都清理空目录链（site 可能只残留空目录）
                        RemoveEmptyDirsUpTo(AppConfig.CurrentSiteDir);
                        if (moved)
                        {
                            log("检测到站点资源残留，已备份到: " + backupDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        return "后端目录已存在且不是Git仓库，清理残留失败: " + ex.Message + "\n目录: " + AppConfig.CurrentBackendDir;
                    }
                    if (Directory.Exists(AppConfig.CurrentBackendDir) &&
                        Directory.GetFileSystemEntries(AppConfig.CurrentBackendDir).Length > 0)
                    {
                        return "后端目录已存在且不是Git仓库，无法自动克隆。\n目录: " + AppConfig.CurrentBackendDir;
                    }
                }

                // 首次克隆优先用官方最新版本标签（正式发布版，稳定）；官方源无标签
                // （网络/代理异常）时回退克隆 v3 分支。--single-branch 只拉取目标提交链，
                // 仓库更小、后续 fetch 更快
                string latestTag, latestTagHash;
                string cloneArgs = "clone --branch v3 --single-branch " + VersionRepo + " \"" + AppConfig.CurrentBackendDir + "\"";
                if (GetOfficialLatestTag(gitExe, envPath, out latestTag, out latestTagHash, log))
                {
                    log("首次克隆使用官方最新标签 " + latestTag + ", 正在克隆...");
                    cloneArgs = "clone --branch " + latestTag + " --single-branch " + VersionRepo + " \"" + AppConfig.CurrentBackendDir + "\"";
                }
                else
                {
                    return "未获取到官方版本标签，请检查网络或稍后重试。";
                }
                string cloneOutput = RunCommand(gitExe, cloneArgs, AppConfig.BASE_DIR, envPath);
                if (!Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, ".git")))
                {
                    return "克隆失败:\n" + cloneOutput;
                }
                // 克隆标签时 HEAD 处于 detached 状态：重建 v3 分支指向当前提交，
                // 保证后续版本判断（IsAncestorOfHEAD）与升级重建（checkout -B v3）基于 v3 分支
                string branchOut = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" checkout -f -B v3",
                    AppConfig.CurrentBackendDir, envPath);
                if (branchOut.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "克隆后重建 v3 分支失败:\n" + branchOut;
                }
                log("后端代码克隆完成");
                // 克隆后立即打补丁：无论哪个 Git 源，都从 gitee v3-rebase 分支 cherry-pick rebase 补丁；
                // 补丁分支可能落后官方（官方推进后补丁上下文不匹配会冲突），冲突时自动回退官方纯净版
                // （丢弃补丁）保证首次部署不被补丁阻塞，与升级/启动更新路径的行为一致
                string patchError = ApplyRebasePatchesWithFallback(gitExe, envPath, log);
                if (patchError != null)
                {
                    return patchError;
                }
                // 首次拉取成功：备份官方模板 category.yaml（补丁已应用，备份的是最终就绪状态）
                BackupCategoryYaml(log);
            }

            // 同步远端为官方仓库（保证后续 git pull 指向官方源）
            string setUrl = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" remote set-url origin " + VersionRepo,
                AppConfig.CurrentBackendDir, envPath);
            if (setUrl.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 无 origin 远端时改用 remote add
                RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" remote add origin " + VersionRepo,
                    AppConfig.CurrentBackendDir, envPath);
            }
            return null;
        }

        /// <summary>
        /// 备份后端 config\category.yaml 到面板 CONFIG_DIR（内容不同才覆盖，幂等）：
        /// 该文件是官方 git 跟踪的模板，升级重建分支会覆盖它，用户手工修改可能丢失；
        /// 首次拉取成功、检测到官方新版本、停止服务三个时机调用，保证修改不丢。
        /// 用内容对比而非 git 状态判断（不受 tracked / untracked 与工作区状态干扰），
        /// CONFIG_DIR 无备份或与当前内容不一致时备份覆盖；源文件不存在时静默跳过。
        /// 返回是否实际执行了备份（更新流程用它判断用户是否修改过，决定是否恢复）。
        /// </summary>
        public static bool BackupCategoryYaml(Action<string> log)
        {
            string src = Path.Combine(AppConfig.CurrentMpConfDir, "category.yaml");
            string dest = Path.Combine(AppConfig.CONFIG_DIR, "category.yaml");
            if (!File.Exists(src))
            {
                return false;
            }
            try
            {
                if (File.Exists(dest) && FileBytesEqual(src, dest))
                {
                    return false; // 与现有备份内容一致，无需重复备份
                }
                Directory.CreateDirectory(AppConfig.CONFIG_DIR);
                File.Copy(src, dest, true);
                log("已备份 category.yaml 到 " + dest);
                return true;
            }
            catch (Exception ex)
            {
                log("备份 category.yaml 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 官方更新成功后恢复用户对 category.yaml 的修改：仅当本次更新前实际发生过备份
        /// （检测到新版本时备份内容与 CONFIG_DIR 已有备份不同，即用户修改过）才执行；
        /// 备份与当前 MP_CONF_DIR 内容一致时跳过。
        /// 不加此条件时，用户从未修改也会用旧版官方模板回退官方新模板，必须由调用方
        /// 传入备份是否实际发生，作为“用户修改过”的可靠标记。
        /// </summary>
        public static void RestoreCategoryYaml(Action<string> log, bool backedUp)
        {
            if (!backedUp)
            {
                return; // 用户未修改过，官方新模板应生效
            }
            string backup = Path.Combine(AppConfig.CONFIG_DIR, "category.yaml");
            string dest = Path.Combine(AppConfig.CurrentMpConfDir, "category.yaml");
            if (!File.Exists(backup) || !File.Exists(dest))
            {
                return;
            }
            try
            {
                if (FileBytesEqual(backup, dest))
                {
                    return; // 与当前内容一致，无需恢复
                }
                File.Copy(backup, dest, true);
                log("已恢复 category.yaml 用户修改到 " + dest);
            }
            catch (Exception ex)
            {
                log("恢复 category.yaml 备份失败: " + ex.Message);
            }
        }

        /// 逐字节比较两个文件内容是否完全一致（长度不同直接不等，避免一次性读入大文件）。
        private static bool FileBytesEqual(string path1, string path2)
        {
            FileInfo f1 = new FileInfo(path1);
            FileInfo f2 = new FileInfo(path2);
            if (f1.Length != f2.Length)
            {
                return false;
            }
            using (FileStream s1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream s2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] b1 = new byte[8192];
                byte[] b2 = new byte[8192];
                while (true)
                {
                    int n1 = s1.Read(b1, 0, b1.Length);
                    int n2 = s2.Read(b2, 0, b2.Length);
                    if (n1 != n2) return false;
                    if (n1 == 0) return true;
                    for (int i = 0; i < n1; i++)
                    {
                        if (b1[i] != b2[i]) return false;
                    }
                }
            }
        }

        /// <summary>
        /// 执行升级流程。
        /// </summary>
        /// <param name="log">日志回调（后台线程调用，调用方需自行封送）</param>
        /// <param name="onFinished">流程结束回调：参数1 是否成功，参数2 提示信息</param>
        public static void Upgrade(Action<string> log, Action<bool, string> onFinished)
        {
            log("开始升级版本...");

            // 确保后端代码存在（首次克隆官方 v3 + 打补丁；已存在则同步远端）
            string codeError = EnsureCode(log);
            if (codeError != null)
            {
                onFinished(false, codeError);
                return;
            }

            string envPath = AppConfig.BuildEnvPath();
            string gitExe = Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe");
            // 本次更新前 category.yaml 备份是否实际发生（用户修改过的标记，决定更新成功后是否恢复）
            bool backedUp = false;

            // 先停止服务
            ServiceManager.StopServices(log);
            Thread.Sleep(300);

            // 获取官方仓库（jxxghp）最新标签 hash，与本地对比；有新标签则签出覆盖老 v3 分支
            string output;
            string latestTag, latestTagHash;
            // 版本标签一律以 jxxghp 官方源为准（其他源仅作代码镜像）；官方源无标签时回退官方 v3 分支
            if (GetOfficialLatestTag(gitExe, envPath, out latestTag, out latestTagHash, log))
            {
                string localHash = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" rev-parse HEAD",
                    AppConfig.CurrentBackendDir, envPath).Trim();
                log("官方最新标签: " + latestTag + " (" + ShortHash(latestTagHash) + ")，本地: " + ShortHash(localHash));

                // 本地 HEAD 打过 cherry-pick 补丁后 hash 已不是标签 hash 本身，不能直接比较相等；
                // 以“本地历史是否包含官方最新标签提交”判断是否已是最新
                if (IsAncestorOfHEAD(gitExe, envPath, latestTagHash))
                {
                    log("本地已是最新版本");
                    // 补丁分支有更新（远程最新 rebase 提交时间比 app.ini 记录新）时，先像"修复冲突"
                    // 一样强制重建官方 v3 基线（丢弃本地旧补丁残留），再重新 cherry-pick 新补丁
                    if (HasNewPatches(gitExe, envPath, log))
                    {
                        log("检测到补丁分支有更新，先强制重建官方 v3 分支...");
                        string rebuildError = ForceRebuildV3(gitExe, envPath, log);
                        if (rebuildError != null)
                        {
                            onFinished(false, rebuildError);
                            return;
                        }
                        // 已是最新也补打一次补丁：保证补丁完整（幂等，已应用过的自动跳过）
                        string patchError = ApplyRebasePatchesWithFallback(gitExe, envPath, log);
                        if (patchError != null)
                        {
                            onFinished(false, patchError);
                            return;
                        }
                        
                    }
                    output = "Already up to date";
                }
                else
                {
                    log("发现新版本，签出覆盖 v3 分支...");
                    // 升级会重建分支覆盖已跟踪模板，先备份可能被用户修改过的 category.yaml
                    backedUp = BackupCategoryYaml(log);
                    string rebuildError = RebuildV3FromTag(gitExe, envPath, latestTag, latestTagHash, log);
                    if (rebuildError != null)
                    {
                        // 官方提交不可用或签出失败：放弃升级，继续使用当前版本（不视为错误）
                        log("放弃本次升级，继续使用当前版本: " + rebuildError);
                        output = "UPGRADE_SKIPPED";
                    }
                    else
                    {
                        log("v3 分支已更新到 " + latestTag);
                        // 更新后重新打补丁（冲突时自动回退官方纯净版并提示）
                        string patchError = ApplyRebasePatchesWithFallback(gitExe, envPath, log);
                        if (patchError != null)
                        {
                            onFinished(false, patchError);
                            return;
                        }
                        // 官方更新成功：备份含用户修改时覆盖回，保留用户对分类的修改
                        RestoreCategoryYaml(log, backedUp);
                        output = "更新成功";
                    }
                }
            }
            else
            {
                // 官方源（jxxghp）无版本标签时退出升级流程，并弹窗提示用户
                onFinished(false, "未获取到官方版本标签，请检查网络或稍后重试。");
                return;
            }
            log("Git输出: " + output);

            bool failed = false;
            string resultMessage;
            if (output == "UPGRADE_SKIPPED")
            {
                log("官方最新版本不可用，继续使用当前版本");
                resultMessage = "官方最新版本不可用，继续使用当前版本";
            }
            else if (output.IndexOf("Already up to date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     output.Contains("已经是最新的"))
            {
                log("当前已是最新版本");
                resultMessage = "当前已是最新版本";
            }
            else if (output.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     output.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                log("升级出错，请检查网络");
                resultMessage = "升级失败:\n" + output;
                failed = true;
            }
            else
            {
                log("代码更新成功");
                resultMessage = "代码更新成功";
            }

            // 安装/更新 Python 依赖（requirements.txt 缺失时自动改用 uv sync 按 pyproject.toml 安装）
            EnvironmentSetup.InstallRequirements(AppConfig.GetPythonExe(), log);

            // 同步前端 / 认证 / 站点资源（按“更新时强制更新前端资源和后端认证和站点资源”配置决定是否强制覆盖）
            SyncResourcesByConfig(log);

            // 重启服务
            Thread.Sleep(500);
            ServiceManager.StartServices(log);

            if (failed)
            {
                onFinished(false, resultMessage);
            }
            else
            {
                log("升级流程完成");
                onFinished(true, resultMessage + " 服务已重启。");
            }
        }


        /// <summary>
        /// 修复代码冲突：强制签出官方最新标签重建 v3 分支（丢弃本地所有 cherry-pick），
        /// 不再并入 v3-rebase 补丁，直接以官方 v3 分支运行。
        /// 用于启动时更新 / 手动升级检测到新的 rebase 补丁与本地旧补丁冲突时的恢复手段。
        /// </summary>
        /// <param name="log">日志回调（后台线程调用，调用方需自行封送）</param>
        /// <param name="onFinished">流程结束回调：参数1 是否成功，参数2 提示信息</param>
        public static void FixCodeConflict(Action<string> log, Action<bool, string> onFinished)
        {
            log("开始修复代码冲突（强制重建官方 v3，不再并入补丁）...");

            if (!EnsureGitReady(log))
            {
                onFinished(false, "未找到便携版 Git，请先点击\"启动服务\"完成环境准备（自动下载 Git）。");
                return;
            }

            string envPath = AppConfig.BuildEnvPath();
            string gitExe = Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe");

            if (!Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, ".git")))
            {
                onFinished(false, "后端目录不是 Git 仓库，无需修复（首次点击\"启动服务\"会自动克隆并打补丁）。");
                return;
            }

            // 1. 先停止服务：代码目录内文件可能被运行中的进程占用，且修复后需重启服务
            ServiceManager.StopServices(log);
            Thread.Sleep(300);

            // 2. 强制重建官方 v3 基线（清理残留 cherry-pick + 获取官方标签 + checkout -B v3 + HEAD 校验）
            string rebuildError = ForceRebuildV3(gitExe, envPath, log);
            if (rebuildError != null)
            {
                onFinished(false, rebuildError);
                return;
            }
            log("本地 cherry-pick 已全部丢弃，不再并入 v3-rebase 补丁");

            // 6. 重新安装/更新 Python 依赖（代码版本变化，依赖可能变更）
            EnvironmentSetup.InstallRequirements(AppConfig.GetPythonExe(), log);

            // 7. 同步前端 / 认证 / 站点资源（按“更新时强制更新前端资源和后端认证和站点资源”配置决定是否强制覆盖）
            SyncResourcesByConfig(log);

            // 8. 重启服务
            Thread.Sleep(500);
            ServiceManager.StartServices(log);

            log("代码冲突修复完成");
            onFinished(true, "代码冲突已修复（已强制重建官方最新 v3，未并入补丁），服务已重启。");
        }

        /// <summary>
        /// 按配置同步资源（手动升级 / 代码冲突时点我流程共用，在服务重启前调用）：
        /// 勾选了“更新时强制更新前端资源和后端认证和站点资源”（默认勾选）时——
        /// 1. 前端资源即使版本号相同也重新下载覆盖：官方前端可能对同一版本号重新发布不同
        ///    内容的 dist.zip（版本号不变、内容更新），仅按版本号比较会漏更；本地版本高于
        ///    要求时不覆盖（用户自装的更高版本前端不回退）；
        /// 2. 强制重新下载认证资源（sites.cp314-win_amd64.pyd / sites.cp314t-win_amd64.pyd）
        ///    与站点资源（user.sites.v3.bin），下载失败自动恢复旧文件并记日志，不阻塞流程。
        /// 未勾选时维持原行为：前端按版本号比较；认证 / 站点资源由启动服务流程按缺失补下载。
        /// </summary>
        private static void SyncResourcesByConfig(Action<string> log)
        {
            if (!AppSettings.Current.ForceUpdateResources)
            {
                EnvironmentSetup.EnsureFrontend(log);
                return;
            }
            log("已勾选\"更新时强制更新前端资源和后端认证和站点资源\"，强制刷新资源...");
            EnvironmentSetup.EnsureFrontend(log, true);
            EnvironmentSetup.RefreshSiteFiles(log);
        }

        /// 强制重建官方 v3 基线（"修复代码冲突"的核心步骤，升级流程中有新补丁/补丁冲突时复用）：
        /// 1. 清理残留的 cherry-pick 进行中状态（上次冲突中止失败时遗留，会阻塞 checkout）；
        /// 2. 获取官方仓库最新标签 hash（强制重建的基准）；
        /// 3. checkout -B v3 重建分支，本地 cherry-pick 全部丢弃；
        /// 4. 校验签出结果（checkout 输出可能不含 fatal 但实际失败，以 HEAD 是否等于标签 hash 为准）。
        /// 返回错误信息，null 表示成功。
        private static string ForceRebuildV3(string gitExe, string envPath, Action<string> log)
        {
            // 清理可能残留的 cherry-pick 进行中状态（上次冲突中止失败时遗留，会阻塞 checkout）
            string cherryPickHead = Path.Combine(AppConfig.CurrentBackendDir, ".git", "CHERRY_PICK_HEAD");
            if (File.Exists(cherryPickHead))
            {
                log("检测到未完成的 cherry-pick，先中止清理");
                RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" cherry-pick --abort",
                    AppConfig.CurrentBackendDir, envPath);
            }

            // 获取官方仓库最新标签 hash（强制重建的基准）
            string latestTag, latestTagHash;
            if (!GetOfficialLatestTag(gitExe, envPath, out latestTag, out latestTagHash, log))
            {
                return "官方源未找到版本标签，无法强制重建 v3 分支。";
            }
            log("官方最新标签: " + latestTag + " (" + ShortHash(latestTagHash) + ") 正在克隆...");

            // 强制签出 v3 最新标签 hash：checkout -f -B v3 重建分支（-f 强制覆盖工作树/index
            // 残留，如预演/手动修改等，保证签出结果与官方标签完全一致），本地 cherry-pick 全部丢弃
            string rebuildError = RebuildV3FromTag(gitExe, envPath, latestTag, latestTagHash, log);
            if (rebuildError != null)
            {
                return rebuildError;
            }

            // 校验签出结果：checkout 输出可能不含 fatal 但实际失败（如本地修改被拒），以 HEAD 为准
            string head = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" rev-parse HEAD",
                AppConfig.CurrentBackendDir, envPath).Trim();
            if (!string.Equals(head, latestTagHash, StringComparison.OrdinalIgnoreCase))
            {
                return "强制签出失败，当前 HEAD 与官方标签不一致 (" + ShortHash(head) + ")，请查看日志。";
            }
            log("已强制签出官方 " + latestTag + "，本地 cherry-pick 已全部丢弃");
            // 已回退官方纯净版（丢弃补丁）：清除当前版本目录的补丁同步时间记录
            // （last_rebase_patch_time_v3/_t），否则旧记录会让下次 HasNewPatches 在补丁分支
            // 无新提交时误判“无新补丁”而跳过补丁；清空后按“未记录（首次）”处理，下次升级/
            // 启动更新会重新拉取并并入补丁
            AppSettings.Current.CurrentLastRebasePatchTime = "";
            AppSettings.Current.Save();
            return null;
        }

        /// 判断补丁分支（v3-rebase）是否有新补丁：拉取补丁分支（gitee 源优先，wlj 源备用），
        /// 取远程最新 rebase 提交的提交时间（committer 时间——重做/amend 补丁会更新），与当前
        /// 版本目录在 app.ini 记录的“上次成功同步的补丁提交时间”对比（v3/V3T 分开记录，
        /// 切换版本互不污染），远程时间更新即有新补丁。无记录（该目录从未同步过）视为有新补丁。
        private static bool HasNewPatches(string gitExe, string envPath, Action<string> log)
        {
            // 拉取补丁分支（与 ApplyRebasePatches 相同：双源回退）
            string fetchOutput = null;
            foreach (string pr in PatchRepos)
            {
                fetchOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" fetch " + pr + " " + PatchBranch,
                    AppConfig.CurrentBackendDir, envPath);
                if (fetchOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    break;
                }
                log("补丁源不可用 (" + pr + ")，尝试下一源...");
            }
            if (fetchOutput == null || fetchOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                log("拉取补丁分支失败，按无新补丁处理: " + fetchOutput);
                return false;
            }

            // 远程补丁分支最新 rebase 提交的提交时间（unix 时间戳）
            string ctOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" log -1 --grep=\"" + @"^rebase$" + "\" --format=%ct FETCH_HEAD",
                AppConfig.CurrentBackendDir, envPath).Trim();
            long remoteUnix;
            if (ctOutput.Length == 0 || ctOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                !long.TryParse(ctOutput, out remoteUnix))
            {
                log("补丁分支无 rebase 提交或获取时间失败，按无新补丁处理");
                return false;
            }

            DateTime remoteTime = UnixTimeToLocal(remoteUnix);
            DateTime lastTime;
            if (!DateTime.TryParse(AppSettings.Current.CurrentLastRebasePatchTime, out lastTime))
            {
                log("未记录补丁同步时间（首次），需要同步补丁");
                return true;
            }
            log("补丁分支最新提交: " + remoteTime.ToString("yyyy-MM-dd HH:mm:ss") +
                "，本地记录: " + lastTime.ToString("yyyy-MM-dd HH:mm:ss"));
            return remoteTime > lastTime;
        }

        /// 把 unix 时间戳转换为本地时间。
        private static DateTime UnixTimeToLocal(long unix)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime();
        }

        /// 同步补丁并带兜底：cherry-pick 失败（如补丁与本地历史冲突）时，自动像"修复冲突"
        /// 一样强制重建官方 v3 纯净版（丢弃补丁），并在日志提示，保证升级流程不被补丁卡住。
        /// 返回错误信息（补丁失败且回退重建也失败），null 表示代码已就绪（带补丁或已回退官方版）。
        private static string ApplyRebasePatchesWithFallback(string gitExe, string envPath, Action<string> log)
        {
            string patchError = ApplyRebasePatches(gitExe, envPath, log);
            if (patchError != null)
            {
                // 仅 cherry-pick 冲突/失败（应用阶段）时回退重建官方纯净版；
                // 补丁源拉取失败（网络问题）不重建——重建无意义且会丢失已应用的补丁
                if (patchError.IndexOf("cherry-pick 失败", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return patchError;
                }

                // 冲突兜底：强制重建官方 v3 纯净版（丢弃补丁），日志提示
                log("补丁 cherry-pick 失败（可能冲突），强制回退官方 v3 纯净版: " + patchError);
                string fallbackError = ForceRebuildV3(gitExe, envPath, log);
                if (fallbackError != null)
                {
                    log("回退重建失败: " + fallbackError);
                    return fallbackError;
                }
                log("已回退到官方 v3 纯净版（未并入补丁），可检查补丁源后重试");
                return null;
            }
            return null;
        }

        /// git 是否就绪（便携版 Git 缺失时提示先点击"启动服务"完成环境准备）。
        private static bool EnsureGitReady(Action<string> log)
        {
            if (File.Exists(Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe")))
            {
                return true;
            }
            log("错误: 未找到便携版 Git，请先点击\"启动服务\"完成环境准备（自动下载 Git）");
            return false;
        }

        /// <summary>
        /// 检查更新（应用启动流程使用）：仅负责获取官方标签、对比并更新代码，不负责停止服务。
        /// 仅在面板"启动时更新版本"开关开启时调用，此时服务未运行无需停止。
        /// 1. 获取官方仓库 jxxghp/MoviePilot 最新标签指向的 commit hash（不是标签标题版本号，
        ///    同版本号的镜像标签 hash 可能不同，必须以 hash 为准）；
        /// 2. 与本地 v3 分支历史对比（merge-base 包含判断），已包含则跳过；
        /// 3. 未包含时，从官方仓库拉取该标签，在最新标签提交上重建 v3 分支替换老 v3，
        ///    并重新安装 Python 依赖。
        /// </summary>
        public static void CheckUpdateOnStart(Action<string> log)
        {
            log("启动检查更新: 获取官方仓库最新标签...");

            if (!EnsureGitReady(log))
            {
                return; // Git 缺失时静默跳过（首次环境准备由"启动服务"触发）
            }

            string envPath = AppConfig.BuildEnvPath();
            string gitExe = Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe");
            // 本次更新前 category.yaml 备份是否实际发生（用户修改过的标记，决定更新成功后是否恢复）
            bool backedUp = false;

            if (!Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, ".git")))
            {
                log("后端目录不是Git仓库，跳过启动更新");
                return;
            }

            // 1. 从 jxxghp 官方源获取最新版本标签及其 commit hash（其他源仅作代码镜像；官方源无标签时回退官方 v3 分支）
            string latestTag, latestTagHash;
            if (GetOfficialLatestTag(gitExe, envPath, out latestTag, out latestTagHash, log))
            {
                // 2. 本地 v3 分支最新 hash
                string localHash = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" rev-parse HEAD",
                    AppConfig.CurrentBackendDir, envPath).Trim();
                log("最新标签: " + latestTag + " (" + ShortHash(latestTagHash) + ")，本地: " + ShortHash(localHash));

                if (IsAncestorOfHEAD(gitExe, envPath, latestTagHash))
                {
                    log("本地已是最新版本，无需更新");
                    // 补丁分支有更新（远程最新 rebase 提交时间比 app.ini 记录新）时，先像"修复冲突"
                    // 一样强制重建官方 v3 基线（丢弃本地旧补丁残留），再重新 cherry-pick 新补丁
                    if (HasNewPatches(gitExe, envPath, log))
                    {
                        log("检测到补丁分支有更新，先强制重建官方 v3 分支...");
                        string rebuildErr = ForceRebuildV3(gitExe, envPath, log);
                        if (rebuildErr != null)
                        {
                            log(rebuildErr);
                            return;
                        }
                        // 已是最新也补打一次补丁：保证补丁完整（幂等，已应用过的自动跳过）
                        string patchErr = ApplyRebasePatchesWithFallback(gitExe, envPath, log);
                        if (patchErr != null)
                        {
                            log(patchErr);
                        }
                    }
                    // 即使代码已是最新也更新依赖/前端：手动 cherry-pick 或补丁可能引入新依赖
                    EnvironmentSetup.InstallRequirements(AppConfig.GetPythonExe(), log);
                    // 同步前端 / 认证 / 站点资源（按“更新时强制更新前端资源和后端认证和站点资源”配置决定是否强制覆盖）
                    SyncResourcesByConfig(log);
                    log("启动检查更新完成");
                    return;
                }

                log("发现新版本，开始更新...");

                // 3. 从 jxxghp 官方源拉取标签并在其提交上重建 v3 分支（替换老 v3）；
                // 重建会覆盖已跟踪模板，先备份可能被用户修改过的 category.yaml
                backedUp = BackupCategoryYaml(log);
                string rebuildError = RebuildV3FromTag(gitExe, envPath, latestTag, latestTagHash, log);
                if (rebuildError != null)
                {
                    log("跳过本次更新，继续使用当前版本: " + rebuildError);
                    return;
                }
                log("v3 分支已更新到 " + latestTag);
            }
            else
            {
                // 官方源（jxxghp）无版本标签时退出流程
                log("未获取到官方版本标签，请检查网络或稍后重试。");
                return;
            }

            // 4. 更新后重新打补丁：从 gitee v3-rebase 分支 cherry-pick 标题含 rebase 的提交
            //（cherry-pick 冲突时自动强制重建官方 v3 纯净版并在日志提示）
            string patchError = ApplyRebasePatchesWithFallback(gitExe, envPath, log);
            if (patchError != null)
            {
                log(patchError);
                return;
            }
            // 官方更新成功：备份含用户修改时覆盖回，保留用户对分类的修改
            RestoreCategoryYaml(log, backedUp);

            // 5. 重新安装/更新 Python 依赖（requirements.txt 缺失时自动改用 uv sync）
            EnvironmentSetup.InstallRequirements(AppConfig.GetPythonExe(), log);

            // 6. 同步前端 / 认证 / 站点资源（按“更新时强制更新前端资源和后端认证和站点资源”配置决定是否强制覆盖）
            SyncResourcesByConfig(log);

            log("启动检查更新完成");
        }

        /// <summary>
        /// 检测当前运行版本的 MoviePilot 后端是否有官方新版本（仅检测不更新，供启动后的
        /// 右上角"MP有新版本"提示使用）：与"检查MP更新"按钮同一逻辑，直接复用
        /// CheckNewMpVersion（仅取是否更新，忽略失败原因与版本详情）。
        /// Git 缺失 / 后端目录不是 Git 仓库 / 官方源不可用 / 已是最新时均返回 false
        /// （静默不打扰，是否弹窗提示由调用方决定）。
        /// </summary>
        /// <param name="log">日志回调（后台线程调用，调用方需自行封送）</param>
        public static bool HasNewMpVersion(Action<string> log)
        {
            return CheckNewMpVersion(log);
        }

        /// <summary>
        /// 检查当前选择运行版本的本地代码相对官方（jxxghp）最新标签是否落后（供"检查MP更新"按钮使用）：
        /// 与右上角"MP有新版本"提示同一判断逻辑——官方最新标签指向的 commit hash 未包含在本地
        /// v3 分支历史中即视为有新版本（本地打过 cherry-pick 补丁后 hash 不同，必须以
        /// merge-base 包含判断）。仅检测不更新，确认升级仍走 Upgrade（内部自行停止服务、
        /// 重建分支、装依赖并重启服务）。
        /// </summary>
        /// <param name="log">日志回调（后台线程调用，调用方需自行封送）</param>
        /// <returns>true = 检测到官方新版本标签（本地未包含）；false = 无更新或检测失败</returns>
        public static bool CheckNewMpVersion(Action<string> log)
        {
            if (!File.Exists(Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe")))
            {
                return false;
            }
            if (!Directory.Exists(Path.Combine(AppConfig.CurrentBackendDir, ".git")))
            {
                return false; // 后端代码未就绪（未点过"启动服务"）：不提示
            }

            string envPath = AppConfig.BuildEnvPath();
            string gitExe = Path.Combine(AppConfig.GIT_CMD_DIR, "git.exe");
            string latestTag, latestTagHash;
            if (!GetOfficialLatestTag(gitExe, envPath, out latestTag, out latestTagHash, log, false))
            {
                return false; // 官方源无标签（网络/代理异常）：按无更新处理，不打扰
            }
            if (IsAncestorOfHEAD(gitExe, envPath, latestTagHash))
            {
                return false; // 官方最新标签已在本地历史中：已是最新
            }
            log("检测到 MoviePilot 新版本标签: " + latestTag + " (" + ShortHash(latestTagHash) + ")，本地未包含。");
            return true;
        }

        /// <summary>
        /// 判断指定提交 hash 是否已包含在本地 HEAD 历史中（即 hash 是 HEAD 的祖先）。
        /// 打补丁后本地 HEAD 不再是官方标签 hash 本身，版本判断必须以“是否包含”为准，
        /// 否则每次检查都会误判为新版本并重复重建分支。
        /// 浅克隆或对象缺失时 merge-base 报 fatal，视为未包含（走更新流程）。
        /// </summary>
        private static bool IsAncestorOfHEAD(string gitExe, string envPath, string hash)
        {
            string baseOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" merge-base " + hash + " HEAD",
                AppConfig.CurrentBackendDir, envPath).Trim();
            if (baseOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return string.Equals(baseOutput, hash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取 jxxghp 官方源最新版本标签及其 commit hash（其他 Git 源仅作代码镜像，版本判断一律以官方源为准）。
        /// ls-remote 输出形如 "hash\trefs/tags/v3.0.1" 和 "hash\trefs/tags/v3.0.1^{}"（带注释标签的 peeled 提交），
        /// peeled 行优先取标签指向的 commit hash，轻量标签用标签行 hash；按语义化版本取最大者。
        /// 返回 false 表示官方源未找到版本标签（调用方回退官方 v3 分支）。
        /// </summary>
        private static bool GetOfficialLatestTag(string gitExe, string envPath, out string latestTag, out string latestTagHash, Action<string> log, bool isPrintLog=true)
        {
            latestTag = null;
            latestTagHash = null;
            if (isPrintLog) log("正在拉取官方标签...");
            string lsRemote = RunCommand(gitExe, "ls-remote --tags " + VersionRepo, AppConfig.BASE_DIR, envPath);
            Dictionary<string, string> tagCommits = new Dictionary<string, string>();
            foreach (string line in lsRemote.Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string hash = line.Substring(0, tab).Trim();
                string refName = line.Substring(tab + 1).Trim();
                if (!refName.StartsWith("refs/tags/")) continue;

                bool peeled = refName.EndsWith("^{}");
                string tagName = peeled
                    ? refName.Substring(10, refName.Length - 13)
                    : refName.Substring(10);
                if (!IsVersionTag(tagName)) continue;

                if (peeled)
                {
                    // peeled 行是标签指向的 commit hash，优先使用
                    tagCommits[tagName] = hash;
                }
                else if (!tagCommits.ContainsKey(tagName))
                {
                    // 轻量标签无 peeled 行，直接用标签行 hash
                    tagCommits[tagName] = hash;
                }
            }

            if (tagCommits.Count == 0)
            {
                // 输出尾部截断后写入日志，便于排查（网络失败 / 代理不可用 / 标签格式变化）
                string tail = lsRemote.Length > 400 ? lsRemote.Substring(lsRemote.Length - 400) : lsRemote;
                if (isPrintLog) log("ls-remote 未解析到版本标签，输出尾部: " + tail);
                return false;
            }

            foreach (KeyValuePair<string, string> kv in tagCommits)
            {
                if (latestTag == null || CompareVersions(kv.Key, latestTag) > 0)
                {
                    latestTag = kv.Key;
                    latestTagHash = kv.Value;
                }
            }
            return true;
        }

        /// <summary>
        /// 从 jxxghp 官方源拉取最新标签，并在其提交上用 checkout -B v3 重建 v3 分支（替换老 v3）。
        /// 调用前需确保后端已停止。返回错误信息，null 表示成功。
        /// </summary>
        private static string RebuildV3FromTag(string gitExe, string envPath, string latestTag, string latestTagHash, Action<string> log)
        {
            string fetchOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" fetch " + VersionRepo + " tag " + latestTag,
                AppConfig.CurrentBackendDir, envPath);
            if (fetchOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "从官方拉取标签失败:\n" + fetchOutput;
            }
            return RebuildV3FromCommit(gitExe, envPath, latestTagHash, latestTag, log);
        }

        /// <summary>
        /// 把本地 v3 分支强制签出覆盖到指定官方提交（用于标签路线与“无标签回退 fetch v3 分支”路线），
        /// 前置：验证目标提交树对象完整 + 未跟踪文件移到上层 tmp；失败返回错误信息，null 表示成功
        /// </summary>
        private static string RebuildV3FromCommit(string gitExe, string envPath, string commitHash, string label, Action<string> log)
        {
            // 验证目标提交的树对象完整存在：上游强制覆盖/历史重写后，ls-remote 看到的 hash 可能已拉取不到，
            // 对象缺失时 checkout 会报 unable to read tree；此时放弃升级，继续使用当前版本
            string verifyOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" cat-file -e " + commitHash + "^{tree}",
                AppConfig.CurrentBackendDir, envPath);
            if (verifyOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "官方最新提交已被上游移除，无法获取 (" + ShortHash(commitHash) + ")";
            }

            // 前置处理：未跟踪文件（旧版本残留/本地生成）移到上层 tmp 目录备份（已存在则覆盖），
            // 避免未跟踪文件与待签出文件同名导致 checkout 失败，或签出后残留旧文件
            MoveUntrackedFiles(gitExe, envPath, log);

            string checkoutOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" checkout -f -B v3 " + commitHash,
                AppConfig.CurrentBackendDir, envPath);
            if (checkoutOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "重建 v3 分支失败:\n" + checkoutOutput;
            }
            return null;
        }

        /// <summary>
        /// 从 gitee 补丁仓库（vueconfig/MoviePilot）的 v3-rebase 分支 cherry-pick 标题含 rebase
        /// 的提交到本地 v3 分支，每个补丁生成一个本地 rebase 提交；已应用过的补丁自动跳过（幂等）。
        /// 成功后把补丁分支最新提交时间记录到当前版本目录的 app.ini 记录
        /// （last_rebase_patch_time_v3/_t），供下次升级时间对比判断是否有新补丁。
        /// 任何 Git 源克隆/更新后都会执行，保证本地代码带补丁。
        /// 返回错误信息，null 表示成功（无补丁提交也视为成功）。
        /// </summary>
        private static string ApplyRebasePatches(string gitExe, string envPath, Action<string> log)
        {
            // 1. 拉取补丁分支（仅获取提交对象，不切换分支）；gitee 源不可用时自动切换 wlj 源
            string fetchOutput = null;
            foreach (string pr in PatchRepos)
            {
                fetchOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" fetch " + pr + " " + PatchBranch,
                    AppConfig.CurrentBackendDir, envPath);
                if (fetchOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    break;
                }
                log("补丁源不可用 (" + pr + ")，尝试下一源...");
            }
            if (fetchOutput == null || fetchOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "拉取补丁分支失败:\n" + fetchOutput;
            }

            // 2. 筛选提交信息包含 rebase 的提交（忽略大小写），逐个 cherry-pick
            string logOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" log --reverse --grep=\"" + @"^rebase$" + "\" --format=%H FETCH_HEAD",
                AppConfig.CurrentBackendDir, envPath).Trim();
            string[] patches = logOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (patches.Length == 0)
            {
                log("补丁分支无标题含 rebase 的提交，跳过补丁");
                return null;
            }

            foreach (string patchHash in patches)
            {
                string cpOut = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" cherry-pick " + patchHash,
                    AppConfig.CurrentBackendDir, envPath);
                if (cpOut.IndexOf("nothing to commit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // 已应用过（改动已在本地）：跳过并继续
                    log("补丁已应用过，跳过: " + ShortHash(patchHash));
                    RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" cherry-pick --abort",
                        AppConfig.CurrentBackendDir, envPath);
                    continue;
                }
                if (cpOut.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cpOut.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cpOut.IndexOf("CONFLICT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // cherry-pick 失败/冲突：回滚恢复现场，返回错误（由调用方决定是否回退重建官方版）
                    RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" cherry-pick --abort",
                        AppConfig.CurrentBackendDir, envPath);
                    return "cherry-pick 失败: " + patchHash + "\n" + cpOut;
                }
                log("补丁已并入: " + ShortHash(patchHash));
            }

            // 3. 记录本次已同步到的补丁分支最新提交时间（app.ini），供下次升级时间对比
            RecordRebaseSyncTime(gitExe, envPath);
            return null;
        }

        /// 把补丁分支（v3-rebase）最新 rebase 提交的提交时间记录到 app.ini（当前版本目录的
        /// last_rebase_patch_time_v3/_t）。在补丁同步成功（无论补丁是本次应用还是已存在跳过）
        /// 后调用：记录的是“已同步到的补丁时间点”，下次升级远程提交时间不更新即相等，
        /// 视为无新补丁，避免重复重建。
        private static void RecordRebaseSyncTime(string gitExe, string envPath)
        {
            string ctOutput = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" log -1 --grep=\"" + @"^rebase$" + "\" --format=%ct FETCH_HEAD",
                AppConfig.CurrentBackendDir, envPath).Trim();
            long remoteUnix;
            if (ctOutput.Length == 0 || ctOutput.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                !long.TryParse(ctOutput, out remoteUnix))
            {
                return; // 获取失败不写记录（下次重新检测）
            }
            AppSettings.Current.CurrentLastRebasePatchTime = UnixTimeToLocal(remoteUnix).ToString("yyyy-MM-dd HH:mm:ss");
            AppSettings.Current.Save();
        }

        /// <summary>
        /// 把后端目录中未跟踪的文件（git ls-files --others）移到上层 tmp 目录备份，
        /// 已存在同名文件则覆盖。签出新版本前调用，避免同名冲突或旧文件残留。
        /// </summary>
        private static void MoveUntrackedFiles(string gitExe, string envPath, Action<string> log)
        {
            string untracked = RunCommand(gitExe, "-C \"" + AppConfig.CurrentBackendDir + "\" ls-files --others --exclude-standard",
                AppConfig.CurrentBackendDir, envPath);
            string tmpDir = Path.Combine(AppConfig.BASE_DIR, "tmp");
            int count = 0;

            foreach (string line in untracked.Split('\n'))
            {
                string rel = line.Trim();
                if (rel.Length == 0) continue;

                string src = Path.Combine(AppConfig.CurrentBackendDir, rel);
                if (!File.Exists(src)) continue;

                string dst = Path.Combine(tmpDir, rel);
                try
                {
                    string dstDir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                    {
                        Directory.CreateDirectory(dstDir);
                    }
                    if (File.Exists(dst))
                    {
                        // 已存在则覆盖
                        File.Delete(dst);
                    }
                    File.Move(src, dst);
                    count++;
                }
                catch (Exception ex)
                {
                    log("移动未跟踪文件失败: " + rel + " (" + ex.Message + ")");
                }
            }

            if (count > 0)
            {
                log("已移动 " + count + " 个未跟踪文件到: " + tmpDir);
            }
        }

        /// 从指定目录向上删除空目录（直到 BACKEND_DIR 为止），用于清理站点资源残留产生的空目录链。
        private static void RemoveEmptyDirsUpTo(string startDir)
        {
            string dir = startDir;
            while (dir != null && dir.Length >= AppConfig.CurrentBackendDir.Length &&
                   dir.StartsWith(AppConfig.CurrentBackendDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir);
                    }
                    else
                    {
                        break; // 非空即停
                    }
                }
                catch
                {
                    break;
                }
                if (dir.Equals(AppConfig.CurrentBackendDir, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        /// 是否为纯数字版本标签（形如 v3.0.1，至少 x.y 两段）。
        private static bool IsVersionTag(string tag)
        {
            string num = tag.TrimStart('v');
            if (num.Length == 0) return false;
            string[] seg = num.Split('.');
            if (seg.Length < 2 || seg.Length > 3) return false;
            foreach (string s in seg)
            {
                int v;
                if (!int.TryParse(s, out v)) return false;
            }
            return true;
        }

        /// 解析标签版本号（形如 v3.0.1 → {3,0,1}）。
        private static int[] ParseVersion(string tag)
        {
            int[] parts = new int[3];
            string[] seg = tag.TrimStart('v').Split('.');
            for (int i = 0; i < 3 && i < seg.Length; i++)
            {
                int v;
                if (int.TryParse(seg[i], out v)) parts[i] = v;
            }
            return parts;
        }

        /// 比较两个版本标签，返回 t1 相对 t2 的大小（>0 表示 t1 更新）。
        private static int CompareVersions(string t1, string t2)
        {
            int[] v1 = ParseVersion(t1);
            int[] v2 = ParseVersion(t2);
            for (int i = 0; i < 3; i++)
            {
                if (v1[i] != v2[i]) return v1[i].CompareTo(v2[i]);
            }
            return 0;
        }

        /// 取 hash 前 8 位用于日志显示。
        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return "?";
            return hash.Length > 8 ? hash.Substring(0, 8) : hash;
        }

        /// <summary>
        /// 运行命令并合并捕获 stdout/stderr（对应原脚本 2>&1）。
        /// </summary>
        private static string RunCommand(string fileName, string arguments, string workingDir, string envPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // git 输出为 UTF-8：按 UTF-8 解码，避免中文（补丁冲突信息等）按 ANSI 代码页解码乱码
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            // 与 ServiceManager.StartProcess 一致：追加系统 PATH，避免 git 子进程（hook、ssh、外部工具）找不到系统程序
            psi.EnvironmentVariables["PATH"] = envPath + ";" + Environment.GetEnvironmentVariable("PATH");

            using (Process p = Process.Start(psi))
            {
                // 注册到活动进程表：面板退出时统一终止，防止 git 等命令在面板退出后遗留
                EnvironmentSetup.TrackProcess(p);
                try
                {
                    // 收集完整输出供调用方判断，同时逐行按 DEBUG 级别转发到面板日志
                    // （仅配置“打印Debug日志”时显示）：拉取标签 / 克隆 / 打补丁等耗时命令
                    // 执行期间勾选 Debug 时即可看到进度；事件式读取天然避免管道缓冲死锁
                    StringBuilder sb = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) return; sb.AppendLine(e.Data); Form1.Debug(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; sb.AppendLine(e.Data); Form1.Debug(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    // git 命令超时限制为 120 秒（卡死的网络请求应尽快放弃，避免长时间挂住）
                    bool timedOut = !p.WaitForExit(120 * 1000);
                    if (timedOut)
                    {
                        // 卡死兜底：强制终止（Kill 后管道关闭，下方读取必然完成）；返回输出附超时标记，
                        // 调用方按 fatal / 空输出判断走失败路径
                        try { p.Kill(); } catch { }
                    }
                    // 无参 WaitForExit 等待异步管道读取结束（Kill 后管道关闭，读取必然完成）
                    p.WaitForExit();
                    string output = sb.ToString();
                    if (timedOut)
                    {
                        output += Environment.NewLine + "fatal: git 命令超时（120 秒），已强制终止";
                    }
                    return output;
                }
                finally
                {
                    EnvironmentSetup.UntrackProcess(p);
                }
            }
        }
    }
}
