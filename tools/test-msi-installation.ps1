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

try {
    $install = Start-Process -FilePath 'msiexec.exe' -ArgumentList @(
        '/i', "`"$msi`"",
        '/qn',
        '/norestart',
        "`"INSTALLFOLDER=$destination`"",
        '/l*v', "`"$installLog`""
    ) -Wait -PassThru

    if ($install.ExitCode -notin 0, 3010) {
        throw "MSI installation failed with exit code $($install.ExitCode). See $installLog"
    }

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
        $uninstall = Start-Process -FilePath 'msiexec.exe' -ArgumentList @(
            '/x', $productCode,
            '/qn',
            '/norestart',
            '/l*v', "`"$uninstallLog`""
        ) -Wait -PassThru

        if ($uninstall.ExitCode -notin 0, 3010) {
            throw "MSI uninstall failed with exit code $($uninstall.ExitCode). See $uninstallLog"
        }

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
