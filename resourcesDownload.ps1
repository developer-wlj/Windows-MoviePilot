cd MoviePilot
cd app/helper
if (Test-Path "sites.cp311-win_amd64.pyd") {
    $resources_url = "https://api.github.com/repos/jxxghp/MoviePilot-Resources/commits?per_page=1"
    $resources_resp=Invoke-WebRequest -URI $resources_url | ConvertFrom-Json
    $eqVersion = Get-Content "../../../tmp/resourceVersion.txt" -TotalCount 1
    if ($resources_resp[0].sha -eq$eqVersion) {
        Write-Output "��Դ���������"
    } else {
        Write-Output "��⵽��Դ���и���"
        $f="t"
        try {
            $extract_url = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources/sites.cp311-win_amd64.pyd"
            echo "��������sites.cp311-win_amd64.pyd"
            # ��鱸���ļ��Ƿ���ڣ����������ɾ��
            if (Test-Path "sites.cp311-win_amd64.pyd1") {
                Remove-Item "sites.cp311-win_amd64.pyd1" -Force
            }
            Rename-Item -Path "sites.cp311-win_amd64.pyd" -NewName "sites.cp311-win_amd64.pyd1" -Force
            Invoke-WebRequest -URI $extract_url -OutFile "sites.cp311-win_amd64.pyd"
            echo "sites.cp311-win_amd64.pyd���سɹ�"
        } catch {
            echo "sites.cp311-win_amd64.pyd����ʧ��, �Ծ��ļ���������"
            Rename-Item -Path "sites.cp311-win_amd64.pyd1" -NewName "sites.cp311-win_amd64.pyd" -Force
            $f="f"
        }

        try {
            $extract_url1 = "https://raw.githubusercontent.com/jxxghp/MoviePilot-Resources/main/resources/user.sites.bin"
            echo "��������user.sites.bin"
            if (Test-Path "user.sites.bin1") {
                Remove-Item "user.sites.bin1" -Force
            }
            Rename-Item -Path "user.sites.bin" -NewName "user.sites.bin1" -Force
            Invoke-WebRequest -URI $extract_url1 -OutFile "user.sites.bin"
            echo user.sites.bin���سɹ�
        } catch {
            echo "user.sites.bin����ʧ��, �Ծ��ļ���������"
            Rename-Item -Path "user.sites.bin1" -NewName "user.sites.bin" -Force
            $f="f"
        }

        if ($f -eq "t") {
            echo $resources_resp[0].sha>"../../../tmp/resourceVersion.txt"
        }

    }

} else {
    echo "�����ļ� Ϊ�չ�����github���õ��û� ��ǰ�ºõ�, ����ļ����� �������������"
    echo �����ͷ���Դ���ļ�
    Move-Item -Path "../../../tmp/resources/*" -Destination . -Force
}


