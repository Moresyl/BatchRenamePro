[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MsiPath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory)]
    [string] $InstallDirectory
)

$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$destination = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$installLog = Join-Path ([IO.Path]::GetTempPath()) 'BatchRenamePro-msi-install.log'
$uninstallLog = Join-Path ([IO.Path]::GetTempPath()) 'BatchRenamePro-msi-uninstall.log'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)
$view = $database.OpenView("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductCode'")
$view.Execute()
$record = $view.Fetch()

if ($null -eq $record) {
    throw 'The MSI does not contain a ProductCode.'
}

$productCode = $record.StringData(1)
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
$desktopDirectories = @(
    [Environment]::GetFolderPath('CommonDesktopDirectory'),
    [Environment]::GetFolderPath('DesktopDirectory')
) | Where-Object { $_ } | Select-Object -Unique
$desktopShortcuts = $desktopDirectories | ForEach-Object { Join-Path $_ 'Batch Rename Pro.lnk' }
$registryPaths = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$productCode",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$productCode"
)
$installed = $false

function Invoke-WindowsInstaller {
    param(
        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $Operation,

        [Parameter(Mandatory)]
        [string] $LogPath
    )

    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $ArgumentList -PassThru
    if (-not $process.WaitForExit(300000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "MSI $Operation timed out after five minutes. See $LogPath"
    }

    if ($process.ExitCode -notin 0, 3010) {
        throw "MSI $Operation failed with exit code $($process.ExitCode). See $LogPath"
    }
}

try {
    Invoke-WindowsInstaller -Operation 'installation' -LogPath $installLog -ArgumentList @(
        '/i', "`"$msi`"",
        '/qn',
        '/norestart',
        "`"INSTALLFOLDER=$destination`"",
        '/l*v', "`"$installLog`""
    )

    $installed = $true
    $exe = Join-Path $destination 'BatchRenamePro.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "The installed executable was not found at $exe"
    }

    if (-not ($desktopShortcuts | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
        throw "The desktop shortcut was not found in: $($desktopDirectories -join ', ')"
    }

    $uninstallEntry = $registryPaths |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1 |
        ForEach-Object { Get-ItemProperty -LiteralPath $_ }

    if ($null -eq $uninstallEntry) {
        throw "The uninstall entry for $productCode was not found."
    }

    if ($uninstallEntry.DisplayVersion -ne $ExpectedVersion) {
        throw "Expected installed version $ExpectedVersion, found $($uninstallEntry.DisplayVersion)."
    }

    $registeredLocation = [IO.Path]::GetFullPath($uninstallEntry.InstallLocation).TrimEnd('\')
    if ($registeredLocation -ne $destination) {
        throw "Expected InstallLocation '$destination', found '$registeredLocation'."
    }

    Write-Output "Verified MSI $ExpectedVersion at $destination"
    Write-Output "Verified desktop shortcut and InstallLocation registration"
}
finally {
    if ($installed) {
        Invoke-WindowsInstaller -Operation 'uninstall' -LogPath $uninstallLog -ArgumentList @(
            '/x', $productCode,
            '/qn',
            '/norestart',
            '/l*v', "`"$uninstallLog`""
        )

        if (Test-Path -LiteralPath (Join-Path $destination 'BatchRenamePro.exe')) {
            throw "The installed executable remained after uninstalling from $destination"
        }

        if ($desktopShortcuts | Where-Object { Test-Path -LiteralPath $_ }) {
            throw 'The desktop shortcut remained after uninstalling.'
        }

        if ($registryPaths | Where-Object { Test-Path -LiteralPath $_ }) {
            throw "The uninstall entry for $productCode remained after uninstalling."
        }

        Write-Output 'Verified clean MSI uninstall'
    }
}
