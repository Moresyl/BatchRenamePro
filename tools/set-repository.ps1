<#
.SYNOPSIS
    Points the project at its real GitHub repository.

.DESCRIPTION
    The repository slug appears in eighteen places — badge URLs, issue-template links, the security
    policy, the changelog's compare links and the About page's constant. This rewrites all of them
    at once so none is left behind pointing at the placeholder.

    Run it after creating the repository on GitHub, then commit the result.

.PARAMETER Slug
    The new owner/name, for example 'acme/rename-pro'.

.PARAMETER WhatIf
    Report what would change without writing anything.

.EXAMPLE
    pwsh tools\set-repository.ps1 -Slug acme/rename-pro
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$')]
    [string] $Slug
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$placeholder = 'batchrenamepro/batchrenamepro'
if ($Slug -eq $placeholder) {
    Write-Output 'Already set to that.'
    return
}

$root = Split-Path -Parent $PSScriptRoot
$changed = 0

# Everything except build output, the git database and the local scratch folders.
$files = Get-ChildItem -Path $root -Recurse -File -Include *.md, *.yml, *.yaml, *.cs, *.csproj, *.props |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git|\.reference|\.firecrawl|release|TestResults)\\' }

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
    if ($text -notmatch [regex]::Escape($placeholder)) { continue }

    $hits = ([regex]::Matches($text, [regex]::Escape($placeholder))).Count
    $relative = $file.FullName.Substring($root.Length + 1)

    if ($PSCmdlet.ShouldProcess($relative, "replace $hits occurrence(s)")) {
        # -NoNewline: Get-Content -Raw keeps the trailing newline, so Set-Content must not add one.
        Set-Content -LiteralPath $file.FullName -Value $text.Replace($placeholder, $Slug) -Encoding utf8 -NoNewline
    }

    Write-Output ("{0,-44} {1}" -f $relative, $hits)
    $changed += $hits
}

Write-Output ''
Write-Output "$changed occurrence(s) of '$placeholder' -> '$Slug'"
Write-Output 'Review with `git diff`, then rebuild: the About page reads the URL from a constant.'
