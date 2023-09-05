# Windows-MoviePilot
- 基于批处理转exe实现

### 版本命名规则
如 "1.1.0.d0a586a" 
- 1.1.0 指的是原MoviePilot仓库 发布的版本号
- d0a586a 指的是 原MoviePilot仓库 最近一次提交代码的hash
  
### 如何运行
Python3.11支持Win10和Win11平台,Win7不支持, Win8需自测 还有平台必须是64位
1. 用户需自己提前安装好谷歌浏览器(MoviePilot需要检测到chrome环境,chrome必须是默认路径,如果用户手动更改过,需重新安装), 官网地址: https://www.google.com/intl/zh-CN/chrome/
2. 双击安装Windows-MoviePilot包, 完成安装
3. 桌面图标右键-打开文件所在的位置-MoviePilot文件夹-app文件夹-core文件夹-打开config.py文件-配置所需变量
4. 双击桌面MoviePilot.exe运行

### 如何升级作者打包好的版本
1. 桌面图标右键-打开文件所在的位置-启动stop.bat ~如安装目录在系统盘的Program Files或Program Files (x86),需要以管理员身份运行~
2. 升级安装, 会覆盖config.py, 因此需备份配置好的config.py及category.yaml,如果category.yaml使用默认配置,无需备份category.yaml
3. 安装升级包
   
### 如何自己升级版本
- MoviePilot前端文件在nginx-html-MoviePilot-Frontend下, 替换成自己打包好的文件
- MoviePilot后端文件在project-MoviePilot目录下 使用git 或ide方式 进行代码合并

### 如何配置config.py
- 只要填写的值不是空类型 布尔类型和数字类型 其他的值统统需要加""引住
- MoviePilot代码 可能会对config.py 追加新的字段 这时你需要在老的config.py中,也要追加相应字段,或者覆盖安装后 重新配置config

### 关于Windows下填写目录问题
填写形式为 r"C:\dir", 如  
 `DOWNLOAD_PATH: str = r"C:\Users\Default\Downloads"`

 ### 关于窗口闪退问题
 找到log文件, 查看具体错误, 文件在  
 `桌面图标右键-打开文件所在的位置-MoviePilot文件夹-config文件夹-logs文件夹-moviepilot.log`  
 如果没log日志或不是最新的日志,请 桌面图标右键-打开文件所在的位置-MoviePilot文件夹 在路径上输入cmd 并回车,在cmd窗口中输入  
 `..\..\Python3.11\python .\app\main.py` 查看错误  
 如果提示Permission denied字样 cmd需管理员身份运行
 
 
