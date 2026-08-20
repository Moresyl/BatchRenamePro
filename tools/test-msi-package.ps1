[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MsiPath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)

function Get-MsiValue {
    param(
        [Parameter(Mandatory)]
        [string] $Query,

        [int] $Column = 1
    )

    $view = $database.OpenView($Query)
    $view.Execute() | Out-Null
    $record = $view.Fetch()
    $value = if ($null -eq $record) { $null } else { $record.StringData($Column) }

    if ($null -ne $record) {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
    }

    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null
    return $value
}

try {
    $productVersion = Get-MsiValue -Query "SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'"
    if ($productVersion -ne $ExpectedVersion) {
        throw "Expected ProductVersion $ExpectedVersion, found $productVersion."
    }

    $secureProperties = Get-MsiValue -Query "SELECT `Value` FROM `Property` WHERE `Property` = 'SecureCustomProperties'"
    if (($secureProperties -split ';') -notcontains 'ARPINSTALLLOCATION') {
        throw 'ARPINSTALLLOCATION is not registered as a secure public property.'
    }

    $desktopDirectory = Get-MsiValue -Query "SELECT `Directory_` FROM `Shortcut` WHERE `Shortcut` = 'DesktopShortcut'"
    if ($desktopDirectory -ne 'DesktopFolder') {
        throw 'The MSI does not contain the required Desktop shortcut.'
    }

    $startMenuDirectory = Get-MsiValue -Query "SELECT `Directory_` FROM `Shortcut` WHERE `Shortcut` = 'StartMenuShortcut'"
    if ($startMenuDirectory -ne 'ProgramMenuDirectory') {
        throw 'The MSI does not contain the required Start-menu shortcut.'
    }

    $installLocationSource = Get-MsiValue `
        -Query "SELECT `Source` FROM `CustomAction` WHERE `Action` = 'SetARPINSTALLLOCATION'"
    $installLocationTarget = Get-MsiValue `
        -Query "SELECT `Target` FROM `CustomAction` WHERE `Action` = 'SetARPINSTALLLOCATION'"
    if ($installLocationSource -ne 'ARPINSTALLLOCATION' -or $installLocationTarget -ne '[INSTALLFOLDER]') {
        throw 'The MSI does not write INSTALLFOLDER to ARPINSTALLLOCATION.'
    }

    $locationSequence = [int] (Get-MsiValue `
        -Query "SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = 'SetARPINSTALLLOCATION'")
    $costSequence = [int] (Get-MsiValue `
        -Query "SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = 'CostFinalize'")
    $registerSequence = [int] (Get-MsiValue `
        -Query "SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = 'RegisterProduct'")
    if ($locationSequence -le $costSequence -or $locationSequence -ge $registerSequence) {
        throw 'ARPINSTALLLOCATION is not scheduled between CostFinalize and RegisterProduct.'
    }

    $applicationFile = Get-MsiValue -Query "SELECT `FileName` FROM `File` WHERE `File` = 'BatchRenameProExe'"
    if ($applicationFile -notmatch 'BatchRenamePro\.exe$') {
        throw 'The MSI does not contain BatchRenamePro.exe.'
    }

    Write-Output "Verified MSI contract for Batch Rename Pro $ExpectedVersion"
    Write-Output 'Verified executable, Desktop and Start-menu shortcuts, and InstallLocation registration'
}
finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
}
