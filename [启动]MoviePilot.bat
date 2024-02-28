@echo off
chcp 65001
reg add HKCU\Console /v QuickEdit /t REG_DWORD /d 0 /f
echo Windows-MoviePilot-Git for Win
echo 本项目由 developer-wlj 整合
echo 开源地址：https://github.com/developer-wlj/Windows-MoviePilot
echo.

setlocal enableextensions enabledelayedexpansion

set "max_length=260"
set "current_dir=%cd%"

if "!current_dir!"=="!current_dir:~,%max_length%!" (
    goto :go

) else (
    goto :stop
)

endlocal

:go
echo [OK] 目录路径字符上限检测通过

setlocal enabledelayedexpansion

set "current_dir=%cd%"
set "has_space=false"

if not "!current_dir: =!" == "!current_dir!" set "has_space=true"

if !has_space! == true (
    goto :stoo
) else (
    echo [OK] 目录路径字符无空格检测通过
	goto :Az
)

endlocal

:Az
echo.
echo [注意] 请保证当前文件所在目录的 完 整 路 径
echo.
set /p input=输入 yes 回车以继续：
if /i "%input%" == "yes" (
    goto :run
) else (
    goto :soop
)

:stop
echo.
echo [Error] 目录路径字符上限检测未通过
echo [Error] 检查安装目录所在的完整路径字符数总共不超过260个
echo [Error] 请调整过后再次运行安装
goto :end

:stoo
echo.
echo [Error] 目录路径字符无空格检测未通过
echo [Error] 检查安装目录所在的完整路径中是否存在空格
echo [Error] 请删除所有空格后再次运行安装
goto :end

:soop
echo.
echo 未输入yes，退出
goto :end

:run
rem echo ins VC...
rem start /wait VC.exe /quiet /norestart
set PATH=%~dp0git\cmd;%path%
call windows_start.cmd

:end
echo.
pause
