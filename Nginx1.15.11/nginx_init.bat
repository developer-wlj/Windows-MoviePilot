@echo off
cd /d %~dp0


start nginx -c conf/nginx.conf
