<#
.SYNOPSIS
    Analyze differences and cherry-pick suggestions between two git branches.

.DESCRIPTION
    Compares commits and content between a source and target branch, highlights differences,
    and generates cherry-pick commands with optional pre-flight checks.
#>

[CmdletBinding()]
param (
    [Parameter(HelpMessage = "Path to the git repository")]
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
    Push-Location $RepoPath
} catch {
    Write-Error "Invalid repository path: $RepoPath"
    exit 1
}

function Test-GitRepo {
    try {
        git rev-parse --git-dir 2>$null | Out-Null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Show-Commits {
    param($From, $To)
    Write-Host "`n📋 OUTSTANDING COMMITS: $From → $To" -ForegroundColor Green
    Write-Host "Commits in $From not in $To" -ForegroundColor Gray
    Write-Host "-" * 50
    
    $commits = git log "$To..$From" --oneline 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not get commit log. Check branch names."
        return @()
    }
    
    if ($commits) {
        $commits | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
        Write-Host "`n✅ Found $($commits.Count) outstanding commits" -ForegroundColor Green
    } else {
        Write-Host "✅ No outstanding commits - branches are in sync!" -ForegroundColor Green
    }
    return $commits
}

function Show-Diff {
    param($From, $To)
    Write-Host "`n🔍 CONTENT ANALYSIS:" -ForegroundColor Green
    Write-Host "Checking for actual file differences ($To..$From)" -ForegroundColor Gray
    Write-Host "-" * 50
    
    $diffCheck = git diff --quiet "$To..$From" 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ No content differences found!" -ForegroundColor Green
        Write-Host "   The branches have the same file content." -ForegroundColor Gray
        return $null
    } else {
        Write-Host "⚠️  Content differences detected!" -ForegroundColor Yellow
        
        # Show file summary
        $changedFiles = git diff --name-only "$To..$From" 2>$null
        if ($changedFiles) {
            Write-Host "`n📁 Changed files:" -ForegroundColor Yellow
            $changedFiles | ForEach-Object { Write-Host "   $_" -ForegroundColor White }
        }
        
        # Show stats
        Write-Host "`n📊 Change summary:" -ForegroundColor Yellow
        git diff --stat "$To..$From" 2>$null
        return $changedFiles
    }
}

function Show-CherryAnalysis {
    param($From, $To)
    Write-Host "`n🍒 CHERRY-PICK ANALYSIS:" -ForegroundColor Green
    Write-Host "Checking which commits from $From need to be applied to $To" -ForegroundColor Gray
    Write-Host "-" * 50
    
    $cherryStatus = git cherry "$To" "$From" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not perform cherry analysis. Check branch names."
        return @()
    }
    
    if ($cherryStatus) {
        $newCommits = @()
        $appliedCommits = @()
        
        $cherryStatus | ForEach-Object {
            if ($_.StartsWith('+')) {
                $newCommits += $_.Substring(2)
            } elseif ($_.StartsWith('-')) {
                $appliedCommits += $_.Substring(2)
            }
        }
        
        if ($newCommits.Count -gt 0) {
            Write-Host "📌 Commits to cherry-pick ($($newCommits.Count)):" -ForegroundColor Yellow
            $newCommits | ForEach-Object { 
                $commitInfo = git log --oneline -1 $_ 2>$null
                if ($commitInfo) {
                    Write-Host "   + $commitInfo" -ForegroundColor White
                }
            }
        }
        
        if ($appliedCommits.Count -gt 0) {
            Write-Host "`n✅ Already applied commits ($($appliedCommits.Count)):" -ForegroundColor Green
            $appliedCommits | ForEach-Object { 
                $commitInfo = git log --oneline -1 $_ 2>$null
                if ($commitInfo) {
                    Write-Host "   - $commitInfo" -ForegroundColor Gray
                }
            }
        }
        
        return $newCommits
    } else {
        Write-Host "✅ No commits to analyze" -ForegroundColor Green
        return @()
    }
}

function Show-SuggestedActions {
    param($NewCommits, $TargetBranch)
    
    if ($NewCommits.Count -gt 0) {
        Write-Host "`n🚀 SUGGESTED ACTIONS:" -ForegroundColor Magenta
        Write-Host "-" * 50
        Write-Host "To cherry-pick all outstanding commits:" -ForegroundColor Yellow
        Write-Host "git checkout $TargetBranch" -ForegroundColor White
        
        $NewCommits | ForEach-Object {
            Write-Host "git cherry-pick $_" -ForegroundColor White
        }
        
        Write-Host "`nTo see detailed diff:" -ForegroundColor Yellow
        Write-Host "git diff $TargetBranch..$SourceBranch" -ForegroundColor White
    }
}

# Main script execution
try {
    if (-not (Test-GitRepo)) {
        Write-Error "Not in a git repository!"
        exit 1
    }

    # Check for uncommitted changes
    git diff --quiet 2>$null
    $hasWorkingChanges = $LASTEXITCODE -ne 0
    git diff --cached --quiet 2>$null
    $hasStagedChanges = $LASTEXITCODE -ne 0

    if ($hasWorkingChanges -or $hasStagedChanges) {
        Write-Warning "Uncommitted changes detected. Consider stashing or committing them first."
        if (-not $WhatIf) {
            $response = Read-Host "Continue anyway? (y/N)"
            if ($response -ne 'y' -and $response -ne 'Y') {
                Write-Host "Aborted by user." -ForegroundColor Yellow
                exit 0
            }
        }
    }

    Write-Host "Fetching latest changes from $Remote..." -ForegroundColor Yellow
    git fetch $Remote 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not fetch from $Remote. Proceeding with local data."
    }

    Write-Host "`n" + "=" * 60
    Write-Host "DEPLOYMENT STATUS: $TargetBranch → $SourceBranch" -ForegroundColor Cyan
    Write-Host "=" * 60

    # Execute analysis functions
    $outstandingCommits = Show-Commits -From $SourceBranch -To $TargetBranch
    $contentDiff = Show-Diff -From $SourceBranch -To $TargetBranch
    $newCommits = Show-CherryAnalysis -From $SourceBranch -To $TargetBranch

    # Show suggested actions
    if (-not $WhatIf) {
        Show-SuggestedActions -NewCommits $newCommits -TargetBranch $TargetBranch
    } else {
        Write-Host "`n💡 WhatIf Mode: No actions will be suggested" -ForegroundColor Cyan
    }

    Write-Host "`n" + "=" * 60
    Write-Host "ANALYSIS COMPLETE" -ForegroundColor Cyan
    Write-Host "=" * 60

} finally {
    # Return to original directory
    Pop-Location
}