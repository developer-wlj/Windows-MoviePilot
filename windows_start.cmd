@echo off
title MoviePilot
cd /d "%~dp0"
powershell ./webDownload.ps1
if not exist "%~dp0MoviePilot" goto :install
git --version|find "git version">nul&&goto :checkGit
goto :installError



:install
echo "正在使用GIT下载源码..."
git clone -b dev https://gitee.com/vueconfig/MoviePilot.git %~dp0MoviePilot
@REM git clone -b dev https://github.com/developer-wlj/MoviePilot.git %~dp0MoviePilot
echo "通过GIT安装完成，开始检测python执行程序..."
goto :checkPython

:installError
cls
echo "当前运行环境未检测到GIT程序，源码安装失败！请手动下载"
pause
goto :end

:checkGit
echo "检查GIT程序，尝试自动更新源码..."
git --version|find "git version">nul&&goto :pull
cls
echo "当前MoviePilot运行环境未检测到git程序，不支持自动更新。"
echo "推荐您使用git来下载代码库！"
echo "您可以在安装git程序后，在命令行内输入："
echo "git clone https://github.com/developer-wlj/MoviePilot.git"
goto :checkPython

:pull
echo "正在检测源码库的git特征文件..."
if exist "%~dp0MoviePilot\.git" (
    echo "正在为您自动更新..."
    cd MoviePilot
    git fetch --all
    git reset --hard origin/dev
    echo "更新完成！"
    cd ..
) else (
    echo "当前MoviePilot源码，并非通过git拉取，不支持自动更新"
)
echo.
goto :checkPython

:checkPython
if exist "%~dp0Python3.11\python.exe" (set PYTHON_BINARY=%~dp0Python3.11\python.exe) else (set PYTHON_BINARY=python.exe)
echo "Python二进制程序："%PYTHON_BINARY%
dir
powershell ./resourcesDownload.ps1
%PYTHON_BINARY% -V|find "Python 3.11.4">nul&&goto :start
cls
echo "没有检测到Python执行程序！！！"
echo "如果您已下载过Python程序，请在解压缩后，把Python文件夹添加进系统的环境变量。"
echo "或者把Python执行程序，解压缩到当前目录下的Python文件夹。"
echo "脚本运行终止！！！"
pause
goto :end

:start
%PYTHON_BINARY% -V
echo.
cd Nginx1.15.11
start /b nginx_init.bat
cd ..
echo "如果您需要停止程序，请按下组合键：CTRL + C"
echo "已自动为你打开了浏览器"
start "" "http://127.0.0.1:3000"
cd MoviePilot
echo "扫描可安装的包......."
%PYTHON_BINARY% -m pip install -r requirements.txt
cd app/plugins
powershell.exe -Command "$pythonBinary = '%PYTHON_BINARY%'; Get-ChildItem -Path . -Recurse -Filter 'requirements.txt' | ForEach-Object { &$pythonBinary -m pip install -i https://mirrors.aliyun.com/pypi/simple/ -r $_.FullName }"
cd ../..
set PYTHONPATH=.
echo "启动后端中......"
%PYTHON_BINARY% app\main.py
pause
goto :end

:end
rem 结束
echo.
