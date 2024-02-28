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
        $extract_url = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources/sites.cp311-win_amd64.pyd"
        echo 正在下载sites.cp311-win_amd64.pyd
        Invoke-WebRequest -URI $extract_url -OutFile "sites.cp311-win_amd64.pyd"
        $extract_url1 = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources/user.sites.bin"
        echo 正在下载user.sites.bin
        Invoke-WebRequest -URI $extract_url1 -OutFile "user.sites.bin"
        echo $resources_resp[0].sha>"../../../tmp/resourceVersion.txt"
    }

} else {
    echo 正在释放资源包文件
    Move-Item -Path "../../../tmp/resources/*" -Destination . -Force
}

cd ../plugins/
if (Test-Path "../../../tmp/plugins.zip") {
    echo 正在释放插件包资源
    Expand-Archive -Path "../../../tmp/plugins.zip" -DestinationPath .
    Remove-Item -Path "../../../tmp/plugins.zip"

} else {
    $plugin_url="https://api.github.com/repos/jxxghp/MoviePilot-Plugins/commits?per_page=1"
    $plugin_resp=Invoke-WebRequest -URI $plugin_url | ConvertFrom-Json
    $eqVersionByPlugin = Get-Content "../../../tmp/pluginVersion.txt" -TotalCount 1
    if ($plugin_resp[0].sha -eq$eqVersionByPlugin) {
        Write-Output "插件资源无需更新"
    } else {
        echo 检测到插件资源有新版本
        Invoke-WebRequest -Uri "https://github.com/jxxghp/MoviePilot-Plugins/archive/refs/heads/main.zip" -OutFile "MoviePilot-Plugins-main.zip"
        Expand-Archive -Path "MoviePilot-Plugins-main.zip" -DestinationPath "MoviePilot-Plugins-main"
        Copy-Item -Path "MoviePilot-Plugins-main/MoviePilot-Plugins-main/plugins/*" -Destination . -Force -Recurse
        Remove-Item -Path "MoviePilot-Plugins-main.zip"
        Remove-Item -Path "MoviePilot-Plugins-main" -Recurse -Force
        echo $plugin_resp[0].sha>"../../../tmp/pluginVersion.txt"
    }
}


