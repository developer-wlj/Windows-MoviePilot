# Windows-MoviePilot-Git


 **基于MoviePilot原版，优化安装流程并整合为一体包，无需安装其他任何文件即可运行MoviePilot。**  
 前往 [Releases](https://github.com/developer-wlj/Windows-MoviePilot/releases/latest "Releases") 下载
 - 基于99.99%的原版(main.py文件删除2行不适用windows下的代码)
 - 优化/可靠
 - 一键安装(基于批处理)/自动升级(基于git)/启动

 **与 [MoviePilot](https://github.com/jxxghp/MoviePilot) 使用方法相同**
 - 认证环境 需要通过系统环境变量配置
 - 重要变量 通过系统环境或app.env 配置
 - 一般变量 通过系统环境或web或app.env 配置

 **使用方法**
 - 点击 `[启动]MoviePilot.bat` 自动安装 自动升级 自动启动

 **其他事项**
 - 因删除的2行代码, 不适合提交到提到原仓库, 会存在主从仓库
 - 主从仓库同步 每4小时 自动同步一次
 - 用能力的 可自行修改 `windows_start.cmd` git clone 的地址

 ***
 思路来源地址：https://github.com/KiWinger/IYUUPlus-Allinone
