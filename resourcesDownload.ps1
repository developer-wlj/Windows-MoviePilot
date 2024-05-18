cd MoviePilot
cd app/helper
if (Test-Path "sites.cp311-win_amd64.pyd") {
    $resources_url = "https://api.github.com/repos/jxxghp/MoviePilot-Resources/commits?per_page=1"
    $resources_resp=Invoke-WebRequest -URI $resources_url | ConvertFrom-Json
    $eqVersion = Get-Content "../../../tmp/resourceVersion.txt" -TotalCount 1
    if ($resources_resp[0].sha -eq$eqVersion) {
        Write-Output "资源包无需更新"
    } else {
        Write-Output "检测到资源包有更新"
        $f="t"
        try {
            $extract_url = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources/sites.cp311-win_amd64.pyd"
            echo "正在下载sites.cp311-win_amd64.pyd"
            # 检查备份文件是否存在，如果存在则删除
            if (Test-Path "sites.cp311-win_amd64.pyd1") {
                Remove-Item "sites.cp311-win_amd64.pyd1" -Force
            }
            Rename-Item -Path "sites.cp311-win_amd64.pyd" -NewName "sites.cp311-win_amd64.pyd1" -Force
            Invoke-WebRequest -URI $extract_url -OutFile "sites.cp311-win_amd64.pyd"
            echo "sites.cp311-win_amd64.pyd下载成功"
        } catch {
            echo "sites.cp311-win_amd64.pyd下载失败, 以旧文件继续运行"
            Rename-Item -Path "sites.cp311-win_amd64.pyd1" -NewName "sites.cp311-win_amd64.pyd" -Force
            $f="f"
        }

        try {
            $extract_url1 = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources/user.sites.bin"
            echo "正在下载user.sites.bin"
            if (Test-Path "user.sites.bin1") {
                Remove-Item "user.sites.bin1" -Force
            }
            Rename-Item -Path "user.sites.bin" -NewName "user.sites.bin1" -Force
            Invoke-WebRequest -URI $extract_url1 -OutFile "user.sites.bin"
            echo user.sites.bin下载成功
        } catch {
            echo "user.sites.bin下载失败, 以旧文件继续运行"
            Rename-Item -Path "user.sites.bin1" -NewName "user.sites.bin" -Force
            $f="f"
        }

        if ($f -eq "t") {
            echo $resources_resp[0].sha>"../../../tmp/resourceVersion.txt"
        }

    }

} else {
    echo "核心文件 为照顾连接github不好的用户 提前下好的, 因此文件较老 需二次启动更新"
    echo 正在释放资源包文件
    Move-Item -Path "../../../tmp/resources/*" -Destination . -Force
}


