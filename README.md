### Windows-MoviePilot-V2已上线, [点我跳转](https://github.com/developer-wlj/Windows-MoviePilot/blob/v2/README.md)

# Windows-MoviePilot与原版区别
- 基于批处理转exe实现
- 认证变量可以通过配置appWindows.env文件 进行认证
- 启动速度更快 (没有数据解压的过程 )
- 支持配置远程插件仓库, 在线安装插件 注意的是 配置第三方插件仓库后 还需要配置 `IS_ENABLED_REMOTE_REPO=true` 才能使用
- 支持在线更新认证和站点文件, 注意的是 认证文件更新成功 需要手动重启Windows-MoviePilot才能生效
- MP前端面板 内存展示 改为MP实际占用的内存 不是整机占用
- 停机速度更快 

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
Windows-MoviePilot内置Python3.11环境,需要注意是Python3.11不支持Win7及之前的老系统,还有系统必须是64位  
Windows-MoviePilot默认使用3000(前端)和3001端口(后端)
1. 系统必须有Visual C++ Redistributable, 没有的 可点击微软开发者官网的链接https://aka.ms/vs/17/release/VC_redist.x64.exe 下载
2. (步骤2可以先跳过,看看是否能成功运行) 详情请查看[关于app.env如何配置](https://github.com/developer-wlj/Windows-MoviePilot#%E5%85%B3%E4%BA%8Eappenv%E5%A6%82%E4%BD%95%E9%85%8D%E7%BD%AE)
3. 双击桌面MoviePilot快捷图标运行
4. 浏览器 输入 http://127.0.0.1:3000 用户名默认: admin, 密码: 第一次安装，随机生成密码并写入到日志
中
### 如何升级作者打包好的版本
1. 升级安装, 会覆盖category.yaml,如果category.yaml使用默认配置,无需备份category.yaml
2. 安装升级包
 
 ### 关于win托盘图标自动退出问题
 桌面MoviePilot图标右键-打开文件所在的位置-MoviePilot文件夹 在路径上输入cmd 并回车,在cmd窗口中输入  
 `..\..\Python3.11\python .\app\main.py` 并回车，查看错误  
 如果提示Permission denied字样 cmd需管理员身份运行

 ### 关于登录页面提示502问题
可以肯定的是后端启动失败了, 多数原因 appWindows.env配置错误  
如果启动成功, 托盘栏上会有一个MoviePilot图标, 没有托盘图标就代表启动失败  
手动cmd方式启动后端 详情看上述 [关于win托盘图标自动退出问题](https://github.com/developer-wlj/Windows-MoviePilot#%E5%85%B3%E4%BA%8Ewin%E6%89%98%E7%9B%98%E5%9B%BE%E6%A0%87%E8%87%AA%E5%8A%A8%E9%80%80%E5%87%BA%E9%97%AE%E9%A2%98)

 ### 关于如何使用CF IP优选插件问题
 1. 安装自定义Hosts插件和Cloudflare IP优选插件
 2. 自定义Hosts打开启用插件选项  
    在自定义Hosts里面填写如 `1.1.1.1 nexus.test` 注意1.1.1.1可以随意填写,但 要和 Cloudflare IP优选插件里面的 优选IP栏 IP一致,并点击保存
 3. 打开Cloudflare IP优选插件, 在优选IP栏 随意填写一个ip, 如 1.1.1.1  
    IPv4和IPv6 根据自身环境勾选  
    高级参数栏 可填写 `-dd` 注意的是 填写-dd 插件不会测速 运行时长在2分钟左右, 如果不填写-dd 插件会测速 运行时长在10分钟左右  
    打开立即运行一次选项 并点击保存
 4. 如果提示`更新系统hosts文件失败`, 请查看`C:\WINDOWS\System32\drivers\etc` 目录下的hosts文件, 是否设置了只读权限

### 关于app.env如何配置
1. 桌面MoviePilot图标右键-打开文件所在的位置-MoviePilot文件夹-config文件夹-找到app.env
2. 在当前目录复制一份app.env并重命名为appWindows.env
3. 打开appWindows.env配置所取变量  
提醒: 配置appWindows.env, 之后的升级安装 不会覆盖用户手动创建的文件,因此不需要备份appWindows.env文件

### 关于看不到网络挂载的盘符问题
- 程序默认安装目录是系统盘的Program Files (x86), 如果在此目录下 运行MP, 需管理员模式,所以Windows-MoviePilot默认申请以管理员身份运行
- 在管理员身份下运行, 是看不到普通用户挂载的盘符, 需要修改注册表(自行搜索)
- 可以前往[Windows-MoviePilot-Git](https://github.com/developer-wlj/Windows-MoviePilot/tree/Windows-MoviePilot-Git) 新项目运行,解决此类问题
