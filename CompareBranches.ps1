<#
.SYNOPSIS
    Analyze differences and cherry-pick suggestions between two git branches.

.DESCRIPTION
    Compares commits and content between a source and target branch, highlights differences,
    and generates cherry-pick commands with optional pre-flight checks.
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory, HelpMessage = "Path to the git repository")]
    [ValidateNotNullOrEmpty()]
    [string]$RepoPath = ".",

    [Parameter(Mandatory, HelpMessage = "Name of the branch containing new work")]
    [ValidateNotNullOrEmpty()]
    [string]$SourceBranch,

    [Parameter(Mandatory, HelpMessage = "Name of the branch you want to merge into")]
    [ValidateNotNullOrEmpty()]
    [string]$TargetBranch,

    [Parameter(HelpMessage = "Remote to fetch from")]
    [string]$Remote = "origin",

    [Switch]$WhatIf
)

# Navigate to the specified repository path
try {
    Push-Location $RepoPath 2>$null
} catch {
    Write-Error "Invalid repository path: $RepoPath"
    exit 1
}

function Test-GitRepo {
    try {
        git rev-parse --git-dir 2>$null | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Show-Commits {
    param($From, $To)
    Write-Host "`n📋 OUTSTANDING COMMITS: branches $From → $To" -ForegroundColor Green
    $commits = git log "$To..$From" --oneline
    if ($commits) {
        $commits | ForEach-Object { Write-Host $_ -ForegroundColor White }
        Write-Host "✅ Found $($commits.Count) outstanding commits" -ForegroundColor Green
    } else {
        Write-Host "✅ No outstanding commits - branches are in sync!" -ForegroundColor Green
    }
    return $commits
}

function Show-Diff {
    param($From, $To)
    Write-Host "`n🔍 CONTENT ANALYSIS: $From..$To" -ForegroundColor Green
    $diff = git diff --find-renames --ignore-all-space "$To..$From"
    if ([string]::IsNullOrWhiteSpace($diff)) {
        Write-Host "✅ No content differences found!" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Content differences detected!" -ForegroundColor Yellow
        Write-Host "`n📁 Changed files:" -ForegroundColor Yellow
        git diff --name-only "$To..$From" | ForEach-Object { Write-Host "   $_" -ForegroundColor White }
        Write-Host "`n📊 Change summary:" -ForegroundColor Yellow
        git diff --stat "$To..$From"
    }
    return $diff
}

function Show-CherryAnalysis {
    param($From, $To)
    Write-Host "`n🍒 CHERRY-PICK ANALYSIS:" -ForegroundColor Green
    $status = git cherry "$To" "$From"
    $new = @(); $applied = @()
    foreach ($line in $status) {
        if ($line.StartsWith('+')) { $new += $line.Substring(2) }
        elseif ($line.StartsWith('-')) { $applied += $line.Substring(2) }
    }
    if ($new) {
        Write-Host "📌 Commits to cherry-pick ($($new.Count)):" -ForegroundColor Yellow
        foreach ($c in $new) {
            Write-Host "   + $(git log --oneline -1 $c)" -ForegroundColor White
        }
    }
    if ($applied) {
        Write-Host "`n✅ Already applied commits ($($applied.Count)):" -ForegroundColor Green
        foreach ($c in $applied) {
            Write-Host "   - $(git log --oneline -1 $c)" -ForegroundColor Gray
        }
    }
    return $new
}

# Main script
if (-not (Test-GitRepo)) {
    Write-Error "Not in a git repository!"
    Pop-Location
    exit 1
}

# Check for uncommitted changes
git diff --quiet
$hasWorkingChanges = $LASTEXITCODE -ne 0
git diff --cached --quiet
$hasStagedChanges = $LASTEXITCODE -ne 0

if ($hasWorkingChanges -or $hasStagedChanges) {
    Write-Error "Uncommitted changes detected—stash or commit them first."
    Pop-Location
    exit 1
}

Write-Host "Fetching latest changes from $Remote ($SourceBranch, $TargetBranch)..." -ForegroundColor Yellow
git fetch $Remote $SourceBranch $TargetBranch

Write-Host "`n" + "=" * 60
Write-Host "DEPLOYMENT STATUS: $TargetBranch -> $SourceBranch" -ForegroundColor Cyan
Write-Host "" + "=" * 60

$outstanding = Show-Commits -From $SourceBranch -To $TargetBranch
$diff = Show-Diff -From $SourceBranch -To $TargetBranch
$newCommits = Show-CherryAnalysis -From $SourceBranch -To $TargetBranch

if ($outstanding -and $diff) {
    Write-Host "`n🚀 SUGGESTED ACTIONS:" -ForegroundColor Magenta
    Write-Host "" + "-" * 50
    Write-Host "git checkout $TargetBranch" -ForegroundColor White
    $cmds = $newCommits | ForEach-Object { "git cherry-pick $_" }
    foreach ($cmd in $cmds) { Write-Host $cmd -ForegroundColor White }
    Write-Host "`nDetailed diff: git diff $TargetBranch..$SourceBranch" -ForegroundColor Yellow
    if (-not $WhatIf) {
        $cmds | Set-Clipboard
        Write-Host "Commands copied to clipboard!" -ForegroundColor Cyan
    } else {
        Write-Host "WhatIf mode: cherry-pick commands not executed." -ForegroundColor Gray
    }
}

Write-Host "`n" + "=" * 60
Write-Host "ANALYSIS COMPLETE" -ForegroundColor Cyan
Write-Host "" + "=" * 60

# Return to original directory
Pop-Location
