<#
.SYNOPSIS
    Dumps the running window's UI Automation tree with on-screen rectangles.

.DESCRIPTION
    A development aid, not part of the product. Screenshots cannot be captured from this kind of
    session, so this is how the layout gets checked: every element with its type, name and rect,
    indented by depth. Sizes in the dump are physical pixels; divide by the scale the header prints
    to get the DIPs the XAML is written in.

.PARAMETER Page
    Navigation entry to select first, by its visible name — '重命名', 'Settings' and so on.

.PARAMETER Filter
    Only print elements whose name or type matches this regular expression.
#>
[CmdletBinding()]
param(
    [string] $Page,
    [string] $Filter,
    [int]    $MaxDepth = 14
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

$process = Get-Process -Name 'BatchRenamePro' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw 'BatchRenamePro is not running.' }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)

$window = $null
foreach ($attempt in 1..40) {
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($window) { break }
    Start-Sleep -Milliseconds 250
}
if (-not $window) { throw 'The window never appeared.' }

$scale = [double]([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width) /
         [double]([System.Windows.Forms.SystemInformation]::VirtualScreen.Width)
$bounds = $window.Current.BoundingRectangle
Write-Output ("window '{0}' {1}x{2} at {3},{4}" -f $window.Current.Name,
    [int]$bounds.Width, [int]$bounds.Height, [int]$bounds.X, [int]$bounds.Y)

# Selecting a page first, so the dump covers the one being worked on.
if ($Page) {
    $byName = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Page)
    $entry = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName)
    if ($entry) {
        $pattern = $null
        if ($entry.TryGetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) {
            $pattern.Select()
            Start-Sleep -Milliseconds 400
        }
    }
    else { Write-Warning "No navigation entry named '$Page'." }
}

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$origin = $window.Current.BoundingRectangle

function Show-Subtree {
    param($Element, [int] $Depth)

    if ($Depth -gt $MaxDepth) { return }

    $info = $Element.Current
    $rect = $info.BoundingRectangle
    # A collapsed or scrolled-out element reports (-inf, -inf, 0, 0); casting that to int throws,
    # so it is reported as hidden instead of formatted as a position.
    $where = if ([double]::IsInfinity($rect.X) -or [double]::IsInfinity($rect.Y)) { 'hidden' }
             else { '{0}x{1} @{2},{3}' -f [int]$rect.Width, [int]$rect.Height,
                                          [int]($rect.X - $origin.X), [int]($rect.Y - $origin.Y) }

    # Relative to the window, which is what the XAML's own numbers describe.
    $line = ('{0}{1} "{2}" {3}' -f
        ('  ' * $Depth),
        $info.ControlType.ProgrammaticName.Replace('ControlType.', ''),
        $info.Name,
        $where)

    if (-not $Filter -or $line -match $Filter) { Write-Output $line }

    $child = $walker.GetFirstChild($Element)
    while ($child) {
        Show-Subtree -Element $child -Depth ($Depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}

Show-Subtree -Element $window -Depth 0
