# 提要
- 由于py低包有3.11换成3.12
- 如安装后无法运行,或运行中出现错误
- 请删除Python3.11目录和project\MoviePilot\app目录(删除这两个目录相当于重装)
- 使用最新安装包重新覆盖安装下
# Windows-MoviePilot-V2与原版V2区别
- 基于批处理转exe实现
- 启动速度更快 (没有数据解压的过程 )
- 支持配置远程插件仓库, 在线安装插件
- 支持在线更新认证和站点文件
- 停机速度更快
- 停机时不会停止用户自己装的nginx或python,只会停止MP关联的nginx和python

### Windows-MoviePilot-V2与Windows-MoviePilot区别
- 去除appWindows.env配置, 恢复使用原版app.env文件
- 不支持认证变量写到app.env，需要写到系统环境变量里
- 去除IS_ENABLED_REMOTE_REPO参数
- 增强核心exe的相关功能
- 去除显示MP实际占用的内存，恢复原版代码（原版是整体占用）
- 支持在WEB页面上重启MP
- v2和v1可以共存 (❗注意: v2安装时不要和v1安装在同一目录)

### 版本命名规则
如 "1.1.0.d0a586a" 
- 1.1.0 指的是原MoviePilot仓库 发布的版本号
- d0a586a 指的是 原MoviePilot仓库 最近一次提交代码的hash
- 每间隔4小时,自动发一次版
- 自动发版可能会打进去新的bug(类似于docker版自动升级dev拉取)
- 因此推荐找对应原项目最新Releases的版本
- 如 原项目最新版本1.9.7 在commit为0fb12c7的地方进行的打包
  ![image](https://github.com/developer-wlj/Windows-MoviePilot/assets/55836679/d0c5f884-9e0d-46a3-9044-0327903eddfb)
  ![image](https://github.com/developer-wlj/Windows-MoviePilot/assets/55836679/53591f14-94aa-4cda-968c-c23bf97fe0ae)
- 那就在本项目找后缀为0fb12c7发行的版本,如果找不到就向上找离0fb12c7最近的提交
  
### 如何运行
Windows-MoviePilot-V2内置Python3.11环境,需要注意是Python3.11不支持Win7及之前的老系统,还有系统必须是64位  
Windows-MoviePilot-V2默认使用3333(前端)和3111端口(后端)
1. 系统必须有Visual C++ Redistributable, 没有的 可点击微软开发者官网的链接https://aka.ms/vs/17/release/VC_redist.x64.exe 下载
2. 双击桌面MoviePilot-V2快捷图标运行
3. 浏览器访问 http://127.0.0.1:3333 用户名默认: admin, 密码: 第一次安装，随机生成密码并写入到日志中

### 如何升级作者打包好的版本
- 升级安装, 会覆盖category.yaml,如果category.yaml使用默认配置,无需备份category.yaml
 
 ### 关于win托盘图标自动退出问题
 找到桌面MoviePilot-V2图标右键-打开文件所在的位置-MoviePilot文件夹 在路径上输入cmd 并回车,在cmd窗口中输入  
 `..\..\Python3.11\python .\app\main.py` 并回车，查看错误  
 如果提示Permission denied字样 cmd需管理员身份运行

 ### 关于登录页面提示502问题
- 如果你看到托盘栏中的MP图标未退出(需要把鼠标放到托盘栏MP图标上,Windows会自动刷新托盘栏),说明MP还未启动完成  
- 没有托盘图标就代表启动失败  手动cmd方式启动后端查看错误 详情看上述 [关于win托盘图标自动退出问题](https://github.com/developer-wlj/Windows-MoviePilot/tree/v2?tab=readme-ov-file#%E5%85%B3%E4%BA%8Ewin%E6%89%98%E7%9B%98%E5%9B%BE%E6%A0%87%E8%87%AA%E5%8A%A8%E9%80%80%E5%87%BA%E9%97%AE%E9%A2%98)

### 关于看不到网络挂载的盘符问题
- 程序默认安装目录是系统盘的Program Files (x86), 如果在此目录下 运行MP, 需管理员模式,所以Windows-MoviePilot-V2默认申请以管理员身份运行
- 在管理员身份下运行, 是看不到普通用户挂载的盘符, 需要修改注册表(自行搜索)
- 可以把Windows-MoviePilot-V2安装非系统盘上,然后启动MP时,取消申请的管理员身份运行,以用户身份运行

### 关于WEB中的重启和托盘栏中的重启
- WEB中的重启 在停机时会给停机信号 业内叫优雅停机 再启动python 优点: 停机时,服务中正在处理的任务 可以保证处理完成 缺点: 偶尔存在长达几分钟停止过程 期间无法被访问
- 托盘栏中的重启 是直接杀死MP关联的python进程然后再启动python 优点: 停机速度快 缺点: 会有数据丢失的风险
- 注意托盘栏中的退出 是直接杀死MP关联的python进程

 
 
