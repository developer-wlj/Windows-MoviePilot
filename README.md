# Windows-MoviePilot
- 基于批处理转exe实现

### 版本命名规则
如 "1.1.0.d0a586a" 
- 1.1.0 指的是原MoviePilot仓库 发布的版本号
- d0a586a 指的是 原MoviePilot仓库 最近一次提交代码的hash
  
### 如何运行
Windows-MoviePilot内置Python3.11环境,需要注意是Python3.11不支持Win7及之前的老系统,还有系统必须是64位  
Windows-MoviePilot默认使用3000(前端)和3001端口(后端)
1. 用户需自己提前安装好谷歌浏览器(MoviePilot需要检测到chrome环境,chrome必须是默认路径,如果用户手动更改过,需重新安装), 官网地址: https://www.google.com/intl/zh-CN/chrome/
2. 双击安装Windows-MoviePilot包, 完成安装
3. (步骤3可以先跳过,看看是否能成功运行)桌面图标右键-打开文件所在的位置-MoviePilot文件夹-app文件夹-core文件夹-打开config.py文件-配置所需变量
4. 双击桌面MoviePilot.exe运行
5. 手动打开浏览器 输入 http://127.0.0.1:3000 用户名默认: admin, 密码默认: password

### 如何升级作者打包好的版本
1. ~桌面图标右键-打开文件所在的位置-启动stop.bat 如安装目录在系统盘的Program Files或Program Files (x86),需要以管理员身份运行~
2. 升级安装, 会覆盖config.py, 因此需备份配置好的config.py及category.yaml,如果category.yaml使用默认配置,无需备份category.yaml
3. 安装升级包
   
### 如何自己升级版本
- MoviePilot前端文件在nginx-html-MoviePilot-Frontend下, 替换成自己打包好的文件
- MoviePilot后端文件在project-MoviePilot目录下 使用git 或ide方式 进行代码合并

### 如何配置config.py
- 示例请参照 [config.example.py](https://github.com/developer-wlj/Windows-MoviePilot/blob/main/config.example.py)
- 很重要的一点! 很重要的一点!! 很重要的一点!!! 请不要删除config.py你没有使用到的变量, 删除可能无法启动服务
- 只要填写的值不是None、布尔类型和数字类型 其他的值统统需要加""引住
- MoviePilot代码 可能会对config.py 追加新的字段, 如果你使用之前配置的config.py,这时你需要在老的config.py中,也要追加相应字段,或者覆盖安装后 重新配置config.py  
![photo_2023-09-17_21-46-03](https://github.com/developer-wlj/Windows-MoviePilot/assets/55836679/3a237a5d-7b16-4f1a-8313-fa45710a94c5)  
如图所示, 提示 `AttributeError: 'Settings' object has no attribute 'xxxx'` 就是老的config.py不能在新版本中使用, 需重新配置


### 关于Windows下填写目录问题
填写形式为 r"C:\dir", 如  
 `DOWNLOAD_PATH: str = r"C:\Users\Default\Downloads"`  
 如果填写是根目录 改为  
 `DOWNLOAD_PATH: str = r"C:"` 或 `DOWNLOAD_PATH: str = r"C:\\"`
 
 ### 关于窗口闪退问题
 找到log文件, 查看具体错误, 文件在  
 `桌面图标右键-打开文件所在的位置-MoviePilot文件夹-config文件夹-logs文件夹-moviepilot.log`  
 如果没log日志或不是最新的日志,请 桌面图标右键-打开文件所在的位置-MoviePilot文件夹 在路径上输入cmd 并回车,在cmd窗口中输入  
 `..\..\Python3.11\python .\app\main.py` 并回车，查看错误  
 如果提示Permission denied字样 cmd需管理员身份运行

 ### 关于登录页面提示502问题
可以肯定的是后端启动失败了, 多数原因 config.py配置错误  
如果启动成功, 会有一个MoviePilot窗口, 没有窗口就代表启动失败  
手动cmd方式启动后端 详情看上述 关于窗口闪退问题 cmd方式

 ### 关于如何使用CF IP优选插件问题
 1. 安装自定义Hosts插件和Cloudflare IP优选插件
 2. 自定义Hosts打开启用插件选项  
    在自定义Hosts里面填写如 `1.1.1.1 nexus.test` 注意1.1.1.1可以随意填写,但 要和 Cloudflare IP优选插件里面的 优选IP栏 IP一致,并点击保存
 3. 打开Cloudflare IP优选插件, 在优选IP栏 随意填写一个ip, 如 1.1.1.1  
    IPv4和IPv6 根据自身环境勾选  
    高级参数栏 可填写 `-dd` 注意的是 填写-dd 插件不会测速 运行时长在2分钟左右, 如果不填写-dd 插件会测速 运行时长在10分钟左右  
    打开立即运行一次选项 并点击保存
 4. 如果提示`更新系统hosts文件失败`, 请查看`C:\WINDOWS\System32\drivers\etc` 目录下的hosts文件, 是否设置了只读权限


 
 
