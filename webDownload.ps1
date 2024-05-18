if (Test-Path "tmp\dist.zip") {
    Write-Host "正在释放WEB资源"
    # 解压
    Expand-Archive tmp\dist.zip -DestinationPath .\tmp
    Copy-Item -Path tmp\dist\* -Destination "Nginx1.15.11\html\MoviePilot-Frontend" -Force -Recurse
    Remove-Item -Path "tmp\dist.zip"
    Remove-Item -Path "tmp\dist" -Recurse
} else {
    $web_url="https://api.github.com/repos/jxxghp/MoviePilot-Frontend/releases/latest"
    $web_resp=Invoke-WebRequest -URI $web_url | ConvertFrom-Json
    $eqVersionByWeb = Get-Content "tmp\currentWebVersion.txt" -TotalCount 1
    if ($web_resp.id -eq$eqVersionByWeb) {
        Write-Output "前端WEB资源无需更新"
    } else {
        Write-Output "检测到前端WEB资源有新版本"
        echo $web_resp.name
        $download_url=$web_resp.assets.browser_download_url
        echo $download_url
        Write-Host "前端 Downloading..."
        try {
            # 下载
            Invoke-WebRequest -URI $download_url -OutFile tmp\dist.zip
            # 获取下载文件的尺寸
            $downloaded_file_size = (Get-ChildItem -Path tmp\dist.zip -File).Length
            # 比较文件的大小
            $expected_file_size =$web_resp.assets[0].size
            if ($downloaded_file_size -eq$expected_file_size) {
                Write-Host "Extracting zip"
                # 解压
                Expand-Archive tmp\dist.zip -DestinationPath .\tmp
                Copy-Item -Path tmp\dist\* -Destination "Nginx1.15.11\html\MoviePilot-Frontend" -Force -Recurse
                Remove-Item -Path "tmp\dist.zip"
                Remove-Item -Path "tmp\dist" -Recurse
                echo $web_resp.id>"tmp/currentWebVersion.txt"
            } else {
                Write-Host "下载失败, 请重新启动"
                Exit 1
            }
        } catch {
            echo "前端下载失败, 如果是首次运行,请二次启动, 如果不是 将以旧文件启动前端"
        }


    }
}



