# MoviePilot-V3 服务管理面板（Windows）

基于 **.NET Framework 4.8** 构建的 MoviePilot v3 一键管理面板：托盘图标 + 可视化界面，傻瓜式启动 / 停止 / 重启全套服务（nginx 前端 + Python 后端），首次使用自动下载便携版运行环境，无需手动配置任何命令行。

## 运行环境要求

- Windows 10 / 11 64 位（已在 Windows 24H2 验证）
- 必须安装 **.NET Framework 4.8 运行时**（本程序基于 4.8 构建，缺省会无法启动）

### 检测是否已安装 .NET Framework 4.8

PowerShell 窗口执行：

```powershell
(Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -Name Release).Release -ge 528040
```

返回 `True` 表示已安装 4.8（Release 值大于等于 528040）。

或 CMD 窗口执行：

```cmd
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
```

输出中 `Release` 的十六进制值大于等于 `0x528040`（即 528040）即为已安装 4.8。

未安装时请到微软官网下载：

> https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48

### 其他说明

- 建议运行到非系统盘（如 D 盘），避免 Program Files 目录的权限问题
- **本程序不是安装包**：`MoviePilot-V3.exe` 是免安装的绿色单文件程序，直接双击即可运行，无需安装向导；请自行创建运行目录（如 `D:\MoviePilot`）并把 exe 放入其中运行，程序会在 exe 同级目录生成 config、runtime、server、mp-web、logs、tmp 等目录
- 首次启动服务时需要联网下载便携版组件（见下文）

## 编译（源码构建）

### 环境准备

- Windows 10 / 11 64 位
- **Visual Studio 2022（建议 17.13 及以上，支持 .slnx 解决方案格式）**，安装时勾选「**.NET 桌面开发**」工作负载（内含 .NET Framework 4.8 目标包与 MSBuild）
- 或单独安装 **.NET Framework 4.8 Developer Pack** + **Build Tools for Visual Studio**（MSBuild 命令行构建）

### 命令行编译

```powershell
# 方式一：MSBuild 构建 Release（产物在 bin\Release\）
msbuild MoviePilot-V3.slnx /p:Configuration=Release /restore

# 方式二：dotnet msbuild（等效）
dotnet msbuild MoviePilot-V3.slnx /p:Configuration=Release /restore
```

Debug 构建将 `Configuration` 改为 `Debug`，产物输出到 `bin\` 目录。也可直接用 Visual Studio 打开 `MoviePilot-V3.slnx` 编译。

### 关于打包

- **面板程序为单文件**：`bin\Release\MoviePilot-V3.exe` 无需附带其他文件即可运行
- `app.ico` 与 `config\` 模板（nginx.conf / common.conf）已作为**嵌入资源**编译进 exe：首次运行自动释放到 exe 同级目录（`app.ico`、`config\` 与 exe 平层），已存在时不再释放（保留本地修改）

## 快速开始

> **强烈建议首次运行前先完成配置**：打开面板后先点「配置」，设置好 **代理**（`proxy_type` / `proxy_host` / `proxy_port`）和 **GitHub Token**（`github_token`），再点启动服务。国内网络环境下不配代理，首次下载便携版组件、站点资源与克隆代码可能很慢甚至失败；填写规则见下文「代理」说明。

1. 双击 `MoviePilot-V3.exe` 打开面板（默认显示主窗口，可在配置中改为启动即驻留托盘）
2. 点击 **启动服务**：首次运行会自动完成以下准备（只需一次）：
   - 下载便携版 **nginx**、**Git**、**Python 3.14.7**（压缩包保存在 `tmp目录`，解压到 `runtime目录`）
   - 创建 Python 虚拟环境并安装后端依赖
   - 下载站点资源文件（sites.pyd / user.sites.v3.bin）
   - 克隆后端代码并自动合入 v3-rebase 补丁
3. 等待后端初始化完成后（约 30~60 秒），浏览器访问：`http://127.0.0.1:3000`
4. 默认账号 `admin`，首次密码随机生成，请查看后端日志（`server\config\logs\moviepilot.log`）

![面板主界面](img/main-window.png)

## 配置项说明（config\app.ini）

面板「配置」对话框修改后自动保存到 `config\app.ini`（UTF-8 无 BOM，key=value 格式），可直接用记事本编辑。

![配置对话框](img/config-window.png)

| 参数 | 默认值 | 说明 | 生效方式 |
|---|---|---|---|
| `nginx_port` | `3000` | 前端访问端口（浏览器访问地址） | **保存即生效**：nginx 运行中自动重载；未运行时下次启动生效 |
| `backend_port` | `3001` | Python 后端 API 端口（nginx 反代目标） | nginx 侧**保存即生效**；**后端需重启服务**才能更换监听端口 |
| `github_token` | （空） | GitHub Token：下载站点资源、访问 GitHub 时携带认证头，可提高请求限额 | **保存即生效**（下次下载时使用） |
| `proxy_type` | （空） | 代理类型：`http` / `socks5`，空为关闭 | **保存即生效**：立即写入 git 全局代理（`git config --global http.proxy`），程序内所有下载（curl）同时走代理 |
| `proxy_host` | （空） | 代理 IP 或域名，**只填地址、不要带协议头**，如 `127.0.0.1` | **保存即生效**（同上） |
| `proxy_port` | `0` | 代理端口，如 `10829`（**无用户名 / 密码**，不支持认证型代理） | **保存即生效**（同上） |
| `shutdown_timeout_sec` | `30` | 停止服务时等待后端优雅退出的秒数，超时强制结束（插件较多时可调大，判断方法见下文「优雅退出」） | **保存即生效**（下次停止服务时使用） |
| `status_monitor_sec` | `5` | 面板服务状态检测间隔（秒，1~600），检测 nginx / Python 进程是否存活（方式见下文「状态监控」） | **需重启面板** |
| `start_minimized_to_tray` | `False` | 启动面板时直接驻留系统托盘（不显示主窗口） | **需重启面板** |
| `auto_update_on_start` | `False` | 面板启动时自动检查官方更新（有新版本自动升级并重启服务） | **需重启面板** |
| `auto_start_services` | `False` | 面板启动时自动启动 nginx / Python 服务 | **需重启面板** |

> 说明：`nginx_port` / `backend_port` 修改保存时，面板会更新 nginx 配置模板、同步到 nginx 实际加载的 conf\ 目录并自动重载生效（nginx 未运行时下次启动生效）。
> 
> **面板修改端口会重载 nginx，后端端口需重启服务后生效（MP后端启动时,传入后端端口环境变量 `PORT`）。

### 优雅退出（shutdown_timeout_sec）

停止服务时，面板会先向后端发送停机信号（Ctrl+Break），等待其优雅收尾（停止定时任务 → 停止插件 → 关闭模块运行时 → 停止事件处理 → 停止消息队列），超过 `shutdown_timeout_sec` 仍未退出才强制结束进程。

**如何判断是否被强制杀死**：查看后端日志 `server\config\logs\moviepilot.log`，一次完整的停机日志如下（截取自真实日志）：

```text
【INFO】2026-08-18 15:02:49,461 - scheduler.py - 正在停止定时任务...
【INFO】2026-08-18 15:02:49,461 - scheduler.py - 定时任务停止完成
【INFO】2026-08-18 15:02:49,461 - lifecycle.py - 正在停止所有插件...
【INFO】2026-08-18 15:02:49,461 - lifecycle.py - 插件停止完成
【INFO】2026-08-18 15:02:49,461 - monitor.py - 未启用插件文件修改监测，无需停止
【INFO】2026-08-18 15:02:49,461 - module_manager.py - 正在关闭模块运行时...
【INFO】2026-08-18 15:02:49,461 - module_manager.py - 模块运行时关闭完成
【INFO】2026-08-18 15:02:49,461 - events.py - 正在停止事件处理...
【INFO】2026-08-18 15:02:51,512 - events.py - 事件处理停止完成
【INFO】2026-08-18 15:02:51,512 - message.py - 正在停止消息队列...
【INFO】2026-08-18 15:02:51,512 - message.py - 消息队列已停止
```

- 如果日志在中间截断（没走到「消息队列已停止」），说明停机超时、进程被**强制杀死**，部分任务可能未收尾完毕
- 插件越多，停机收尾耗时越长：请根据后端日志中实际输出的停机耗时，把 `shutdown_timeout_sec` 调整到合理值（略大于日志中的停机总耗时），保存后下次停止服务即生效

### 状态监控（status_monitor_sec）

面板上「Nginx / Python」的服务状态由该参数控制检测频率，默认每 5 秒检测一次，可配置范围 1~600 秒。

检测方式：Nginx 读取其安装目录的 `runtime\Nginx\logs\nginx.pid` PID 文件，仅当 PID 文件存在且对应进程仍存活时才显示「运行中」；Python 后端不依赖 PID 文件，通过系统 PowerShell 查询进程命令行特征（可执行文件位于 `runtime\` 便携版目录且命令行包含 `server\app\main.py`）判断，匹配到任一存活进程即视为运行中。两者都不会把系统里其他同名进程误判为服务。

### 代理（proxy_type / proxy_host / proxy_port）

- `proxy_host` 只填写 IP 或域名，**不要写协议头**（不要填 `http://127.0.0.1`，直接填 `127.0.0.1`）
- 只需填写地址与端口两项即可，**无用户名 / 密码**（认证型代理无法使用）
- 保存后立即写入 git 全局代理并作用于所有下载；本地代理工具（Clash、v2rayN 等）直接填其监听地址与端口即可
- **Python 后端的代理注入**：配置代理后启动后端时，会向后端进程注入 `HTTP_PROXY` / `HTTPS_PROXY` / `ALL_PROXY` 环境变量（大小写同时注入），requests / httpx 等网络库自动走代理；同时注入 `NO_PROXY` **排除常规局域网地址**：本机回环（localhost、127.0.0.1、::1）与私有网段（10.0.0.0/8、172.16.0.0/12、192.168.0.0/16）及链路本地（169.254.0.0/16），保证后端访问本机与局域网内服务时不会被代理劫持

## 命令行用法

面板托盘图标右键菜单与命令行均可控制服务，命令行方式适合脚本、计划任务调用：

![托盘右键菜单](img/tray-menu.png)

```cmd
MoviePilot-V3.exe -c start     # 启动服务（首次运行自动准备环境：下载组件、建虚拟环境、同步代码与补丁）
MoviePilot-V3.exe -c stop      # 停止全部服务（优雅退出，超时强制结束）
MoviePilot-V3.exe -c restart   # 重启服务（先确保环境就绪 → 停止 → 启动；纯重启，不检查更新）
MoviePilot-V3.exe -c upgrade   # 升级版本（与配置窗口「立即升级版本」一致：停止 → 更新代码与补丁 → 安装依赖 → 同步前端 → 重启服务）
```

- 命令执行日志：面板正在运行时实时显示在面板「运行日志」区；面板未运行时写入 `logs\cmd.log`
- `-c` 后接未知命令、或首个参数不是 `-c` 时，提示支持的命令列表后退出
- **restart 特例**：若后端配置 `server\config\app.env` 中设置了 `MOVIEPILOT_AUTO_UPDATE=True`，`-c restart` 将先检查官方更新并升级（等效 Docker 容器重启自动更新），再启动服务
- 不带参数直接双击即打开可视化面板

## 特点

- **傻瓜式可视化**：全部操作只需点击按钮，无需任何命令行知识
- **零手动环境配置**：nginx / Git / Python 便携版自动下载安装，与系统环境互不干扰
- **自动建虚拟环境**：后端运行在独立 venv 中，依赖隔离、可随时重建
- **自动更新与补丁**：重启服务时自动检查官方最新版本，并自动合入 v3-rebase 补丁（幂等，已应用自动跳过）
- **GitHub 加速友好**：支持 Token 与 HTTP / SOCKS5 代理（下载、git 均生效）
- **托盘常驻**：最小化到托盘，状态一目了然；支持开机后手动一键拉起服务
- **优雅停止**：停止时优先发送停机信号等待任务收尾，超时再强制结束，兼顾数据安全与响应速度

## 补丁包说明（v3-rebase）

面板在克隆 / 更新后端代码后，会自动合入 **v3-rebase 补丁**（cherry-pick 方式，幂等，已应用过的自动跳过），在官方 MoviePilot 基础上补充以下能力：

1. **Web 端重启**：可在 MoviePilot 网页后台直接重启后端服务，无需回到面板操作
2. **联动管理面板监控**：后端运行期间维护 PID 文件（供脚本 / 工具查询），面板通过进程命令行特征匹配判断后端存活并定时刷新服务状态
3. **认证与站点资源重启下载**：启动后端前按标记重新下载认证 / 站点资源文件（sites.pyd / user.sites.v3.bin），保证资源版本最新
4. **类似 Docker 的重启自动升级**：重启服务时自动检查官方最新标签，有新版本自动升级并重新打补丁，等效于 Docker 容器重启自动更新

## 升级机制与配置保护

### 升级方式

- **立即升级**：配置窗口点击「立即升级版本」按钮
- **启动时自动升级**：开启配置项「启动时更新版本」（`auto_update_on_start`），面板启动时自动检查并升级
- **命令行升级**：`MoviePilot-V3.exe -c upgrade`

升级流程：停止服务 → 获取官方（jxxghp）最新版本标签并对比本地代码（本地历史包含官方标签即视为已最新）→ 有新版本则从官方拉取标签、重建 v3 分支并重新合入 v3-rebase 补丁 → 更新 Python 依赖 → 同步前端资源 → 重启服务。官方源无版本标签时回退为 `git pull` 更新。

**升级是安全的**：官方最新版本不可用（网络异常、上游强制覆盖等）或升级失败时自动放弃，继续使用当前版本运行，不会中断服务。

### 会不会覆盖用户配置

- **面板配置 `config\app.ini`**（端口、代理、Token 等）：位于 exe 同级目录，**不在代码仓库内，升级完全不影响**
- **后端配置 `server\config\app.env`**（后端端口、认证等）：已被代码仓库的 `.gitignore` 忽略，**git 升级不会触碰**
- **运行时数据**（`server\config` 下的 logs、cache、cookies、temp、user.db 等）：不参与版本控制，升级不动
- **被跟踪的模板文件**（如 `config\category.yaml`）：升级时随官方版本更新；若你手工修改过且与新版内容冲突，git 会拒绝签出、升级自动放弃，**不会静默覆盖你的修改**
- **未跟踪的残留文件**：升级前自动移出到 `tmp` 目录备份，避免干扰签出

## 目录结构

```
MoviePilot-V3\
├── MoviePilot-V3.exe          # 主程序（面板 / 命令行）
├── config\                    # 面板配置（app.ini、nginx.conf、common.conf；模板首启自动释放）
├── app.ico                    # 面板图标（首启自动释放，exe 内已嵌入）
├── runtime\                   # 便携版运行时（首次启动自动准备）
│   ├── Nginx\                 # nginx（配置在 conf\，由面板模板同步）
│   ├── Git\                   # Git 便携版
│   ├── Python3.12.8\          # Python 便携版
│   └── venv\                  # Python 虚拟环境（后端运行于此）
├── server\                    # MoviePilot 后端代码（官方源 + v3-rebase 补丁）
├── mp-web\                    # 前端页面（可替换为自己的构建产物）
├── tmp\                       # 下载缓存（压缩包，可清理）
└── logs\                      # 面板 / 命令行日志（cmd.log）
```

## 常见问题

- **页面 502 / 无法访问**：后端冷启动约需 30~60 秒（初始化数据库、加载插件），稍等片刻刷新即可；也可查看面板日志区的后端启动输出
- **首次下载很慢或失败**：强烈建议先在「配置」中设置好代理与 GitHub Token 再启动服务（国内网络环境几乎必需）
- **端口被占用**：修改 `nginx_port` / `backend_port` 为未占用端口（保存后 nginx 自动重载）
- **排查后端错误（前台运行）**：点击托盘图标右键 →「打开面板目录」，在目录空白处按住 Shift + 鼠标右键 →「在此处打开 PowerShell / 在此处打开 CMD」，输入以下命令，前台运行后端查看报错：

  ```cmd
  runtime\venv\Scripts\activate && python server\app\main.py
  ```

  > 注意：`&&` 是 CMD 写法；PowerShell 5.1 不支持 `&&`，请改用 `;` 连接（`.\runtime\venv\Scripts\activate.ps1; python .\server\app\main.py`），或直接调用虚拟环境内的解释器：`.\runtime\venv\Scripts\python.exe .\server\app\main.py`

## 关于杀毒软件误报

- **VirScan 多引擎在线扫描全部通过**：VirScan 基于已知病毒的 hash 特征库比对，本程序未命中任何已知病毒签名
- **VirusTotal 可能报 `Trojan/Win32.Wacatac`，属于误报**：VirusTotal 部分引擎基于行为（启发式）判断，本程序需要通过 PID 获取进程信息（启动 / 停止 / 监控 nginx 与 Python 服务进程），这类进程管理行为触发了启发式规则
- **程序不向第三方域名发送请求**：全部网络请求仅限 **nginx 官网**（下载 nginx 便携版）与 **GitHub 官网**（下载 Git / Python 便携版、后端代码与站点资源），不包含任何第三方域名
- **代码已开源**：完整源码在本仓库，可自行编译复现，也可交给任意 AI 进行代码审查
- **无数字签名证书**：本程序未购买数字签名证书，因此 Windows SmartScreen 与部分杀毒软件可能提示未知发布者；介意者请勿使用，或自行编译源码运行

## 致谢

本项目基于以下开源项目构建，特此感谢：

- [Nginx](https://nginx.org/)：高性能 Web 服务器与反向代理，提供前端服务
- [Git](https://git-scm.com/)：分布式版本控制系统，用于代码克隆与管理
- [Python](https://www.python.org/)：后端运行环境（Python 3.12.8）
- [Roslyn](https://github.com/dotnet/roslyn) / [MSBuild](https://github.com/dotnet/msbuild)：C# 编译器与构建工具链
- [MoviePilot](https://github.com/jxxghp/MoviePilot)：MoviePilot 核心项目（后端代码）
- [MoviePilot-Frontend](https://github.com/jxxghp/MoviePilot-Frontend)：MoviePilot 前端项目
