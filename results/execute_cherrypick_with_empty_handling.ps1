<#
.SYNOPSIS
    Executes a cherry-pick plan with automatic handling of empty commits
.DESCRIPTION
    This script reads a cherry-pick plan JSON file and executes each step,
    automatically skipping empty commits when encountered.
.PARAMETER PlanFile
    Path to the cherry-pick plan JSON file
.PARAMETER SkipEmpty
    If set, automatically skips empty commits. Otherwise, prompts for action.
.PARAMETER LogFile
    Path to log file for recording skipped commits (default: skipped_commits.log)
.EXAMPLE
    .\execute_cherrypick_with_empty_handling.ps1 -PlanFile "cherrypick_plan.json" -SkipEmpty
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$PlanFile,
    
    [switch]$SkipEmpty,
    
    [string]$LogFile = "skipped_commits.log"
)

# Ensure we're in a git repository
if (-not (Test-Path .git)) {
    Write-Error "Not in a git repository. Please run this script from the repository root."
    exit 1
}

# Check if plan file exists
if (-not (Test-Path $PlanFile)) {
    Write-Error "Plan file not found: $PlanFile"
    exit 1
}

# Read the plan
try {
    $plan = Get-Content $PlanFile -Raw | ConvertFrom-Json
} catch {
    Write-Error "Failed to read plan file: $_"
    exit 1
}

# Initialize log
$logEntries = @()
$startTime = Get-Date
Write-Host "Starting cherry-pick execution at $startTime" -ForegroundColor Cyan
Write-Host "Plan contains $($plan.Count) steps" -ForegroundColor Cyan
Write-Host ""

# Statistics
$stats = @{
    Total = $plan.Count
    Successful = 0
    Skipped = 0
    Failed = 0
    Conflicts = 0
}

# Function to log skipped commits
function Log-SkippedCommit {
    param(
        [string]$CommitSha,
        [string]$Description,
        [string]$Reason
    )
    
    $entry = @{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        CommitSha = $CommitSha
        Description = $Description
        Reason = $Reason
    }
    
    $script:logEntries += $entry
    
    # Append to log file
    $logLine = "$($entry.Timestamp) | $($entry.CommitSha) | $($entry.Description) | $($entry.Reason)"
    Add-Content -Path $LogFile -Value $logLine
}

# Function to handle cherry-pick result
function Handle-CherryPickResult {
    param(
        [object]$Step,
        [int]$ExitCode,
        [string]$Output
    )
    
    if ($ExitCode -eq 0) {
        Write-Host "✓ Successfully cherry-picked!" -ForegroundColor Green
        $script:stats.Successful++
        return $true
    }
    
    # Check for empty commit
    if ($Output -match "The previous cherry-pick is now empty" -or 
        $Output -match "nothing to commit, working tree clean") {
        
        Write-Host "⚠ Empty commit detected" -ForegroundColor Yellow
        
        if ($Step.IsEmpty) {
            Write-Host "  Expected empty commit: $($Step.EmptyReason)" -ForegroundColor DarkYellow
        }
        
        if ($SkipEmpty -or $Step.IsEmpty) {
            Write-Host "  Skipping empty commit..." -ForegroundColor Yellow
            git cherry-pick --skip 2>&1 | Out-Null
            
            Log-SkippedCommit -CommitSha $Step.CommitShas[0] `
                            -Description $Step.Description `
                            -Reason $(if ($Step.EmptyReason) { $Step.EmptyReason } else { "Empty commit" })
            
            $script:stats.Skipped++
            return $true
        } else {
            # Prompt user for action
            Write-Host "  The commit is empty. What would you like to do?" -ForegroundColor Yellow
            Write-Host "    [S]kip this commit" -ForegroundColor Cyan
            Write-Host "    [A]llow empty commit" -ForegroundColor Cyan
            Write-Host "    [Q]uit" -ForegroundColor Cyan
            
            $choice = Read-Host "Choice (S/A/Q)"
            
            switch ($choice.ToUpper()) {
                'S' {
                    git cherry-pick --skip 2>&1 | Out-Null
                    Log-SkippedCommit -CommitSha $Step.CommitShas[0] `
                                    -Description $Step.Description `
                                    -Reason "User chose to skip"
                    $script:stats.Skipped++
                    return $true
                }
                'A' {
                    git commit --allow-empty 2>&1 | Out-Null
                    git cherry-pick --continue 2>&1 | Out-Null
                    $script:stats.Successful++
                    return $true
                }
                'Q' {
                    Write-Host "Aborting cherry-pick operation..." -ForegroundColor Red
                    git cherry-pick --abort 2>&1 | Out-Null
                    return $false
                }
                default {
                    Write-Host "Invalid choice. Aborting..." -ForegroundColor Red
                    git cherry-pick --abort 2>&1 | Out-Null
                    return $false
                }
            }
        }
    }
    
    # Check for merge conflicts
    if ($Output -match "conflict" -or $Output -match "CONFLICT") {
        Write-Host "✗ Merge conflict detected!" -ForegroundColor Red
        Write-Host "  Please resolve the conflicts manually, then run:" -ForegroundColor Yellow
        Write-Host "    git cherry-pick --continue" -ForegroundColor Cyan
        Write-Host "  Or to abort:" -ForegroundColor Yellow
        Write-Host "    git cherry-pick --abort" -ForegroundColor Cyan
        $script:stats.Conflicts++
        return $false
    }
    
    # Other errors
    Write-Host "✗ Cherry-pick failed!" -ForegroundColor Red
    Write-Host "  Error: $Output" -ForegroundColor Red
    $script:stats.Failed++
    return $false
}

# Execute the plan
$stepNumber = 1
foreach ($step in $plan) {
    Write-Host "[$stepNumber/$($plan.Count)] Executing: $($step.Description)" -ForegroundColor Cyan
    
    if ($step.IsEmpty) {
        Write-Host "  ⚠ Note: This commit is expected to be empty" -ForegroundColor DarkYellow
    }
    
    Write-Host "  Command: $($step.GitCommand)" -ForegroundColor DarkGray
    
    # Execute the git command
    $gitArgs = $step.GitCommand.Substring(4).Trim() # Remove 'git ' prefix
    $result = & git $gitArgs.Split(' ') 2>&1
    $exitCode = $LASTEXITCODE
    
    $success = Handle-CherryPickResult -Step $step -ExitCode $exitCode -Output ($result -join "`n")
    
    if (-not $success) {
        Write-Host ""
        Write-Host "Execution stopped at step $stepNumber" -ForegroundColor Red
        break
    }
    
    Write-Host ""
    $stepNumber++
}

# Summary
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Cherry-pick execution completed at $endTime" -ForegroundColor Cyan
Write-Host "Duration: $($duration.TotalMinutes.ToString('0.##')) minutes" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Total steps:      $($stats.Total)" -ForegroundColor White
Write-Host "  Successful:       $($stats.Successful)" -ForegroundColor Green
Write-Host "  Skipped (empty):  $($stats.Skipped)" -ForegroundColor Yellow
Write-Host "  Conflicts:        $($stats.Conflicts)" -ForegroundColor Red
Write-Host "  Failed:           $($stats.Failed)" -ForegroundColor Red

if ($stats.Skipped -gt 0) {
    Write-Host ""
    Write-Host "Skipped commits have been logged to: $LogFile" -ForegroundColor Yellow
}

# Export detailed log as JSON if there were any skipped commits
if ($logEntries.Count -gt 0) {
    $jsonLogFile = [System.IO.Path]::ChangeExtension($LogFile, ".json")
    $logEntries | ConvertTo-Json -Depth 10 | Out-File $jsonLogFile
    Write-Host "Detailed log saved to: $jsonLogFile" -ForegroundColor Cyan
}
