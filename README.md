# Windows-MoviePilot
- 基于批处理转exe实现

### 如何运行
以win10为例
1. 双击安装包, 完成安装
2. 桌面图标右键-打开文件所在的位置-MoviePilot文件夹-app文件夹-core文件夹-打开config.py文件-配置所需变量
3. 双击桌面MoviePilot.exe运行

### 如何升级作者打包好的版本
1. 桌面图标右键-打开文件所在的位置-启动stop.bat 如安装目录在系统盘的Program Files或Program Files (x86),需要以管理员身份运行
2. 升级安装, 会覆盖config.py, 因此需备份配置好的config.py及category.yaml,如果category.yaml使用默认配置,无需备份category.yaml
3. 安装升级包
   
### 如何自己升级版本
- MoviePilot前端文件在nginx-html-MoviePilot-Frontend下, 替换成自己打包好的文件
- MoviePilot后端文件在project-MoviePilot目录下 使用git 或ide方式 进行代码合并

### 关于Windows下填写目录问题
填写形式为 r"C:\dir", 如  
 `DOWNLOAD_PATH: str = r"C:\Users\Default\Downloads"`

 ### 关于窗口闪退问题
 找到log文件, 查看具体错误, 文件在  
 `桌面图标右键-打开文件所在的位置-MoviePilot文件夹-config文件夹-logs文件夹-moviepilot.log`
 
