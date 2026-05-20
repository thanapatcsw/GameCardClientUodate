# ===================================================================
#  Fix Git & Re-init - Startup City Unity Project
#  This script:
#   1. Removes old .git (which had large Library files in history)
#   2. Re-inits git with Git LFS
#   3. Re-commits without Library/Temp/Logs/Build folders
#  After running, open GitHub Desktop and click Publish.
# ===================================================================

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

Write-Host ""
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "  Fix Git: Startup City Unity Project" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Working folder: $PSScriptRoot" -ForegroundColor Gray
Write-Host ""

# ----- Check Git -----
try {
    $gitVer = git --version
    Write-Host "[OK] $gitVer" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Git not found! Install from https://git-scm.com/download/win" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

# ----- Check Git LFS -----
$hasLfs = $true
try {
    $lfsVer = git lfs version
    Write-Host "[OK] Git LFS: $lfsVer" -ForegroundColor Green
} catch {
    Write-Host "[WARN] Git LFS not found - will skip LFS tracking" -ForegroundColor Yellow
    Write-Host "       (Recommended: install from https://git-lfs.com)" -ForegroundColor Yellow
    $hasLfs = $false
}

Write-Host ""
Write-Host "IMPORTANT: Close GitHub Desktop before continuing!" -ForegroundColor Yellow
$confirm = Read-Host "Have you closed GitHub Desktop? (y/n)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "Cancelled. Please close GitHub Desktop and run again." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host ""
Write-Host "--- Step 1: Remove old .git ---" -ForegroundColor Cyan

if (Test-Path ".git") {
    Remove-Item -Path ".git" -Recurse -Force
    Write-Host "[OK] Old .git removed" -ForegroundColor Green
} else {
    Write-Host "[INFO] No .git found - skipping" -ForegroundColor Gray
}

Write-Host ""
Write-Host "--- Step 2: Re-init Git ---" -ForegroundColor Cyan

git init 2>&1 | Out-Null
git branch -M main
Write-Host "[OK] Repo initialized, branch set to main" -ForegroundColor Green

Write-Host ""
Write-Host "--- Step 3: Setup Git LFS ---" -ForegroundColor Cyan

if ($hasLfs) {
    try {
        git lfs install --local 2>&1 | Out-Null
        git lfs track "*.psd" | Out-Null
        git lfs track "*.ai"  | Out-Null
        git lfs track "*.fbx" | Out-Null
        git lfs track "*.wav" | Out-Null
        git lfs track "*.mp3" | Out-Null
        git lfs track "*.mp4" | Out-Null
        git lfs track "Assets/Fonts/*.asset" | Out-Null
        Write-Host "[OK] LFS tracking configured for large binary files" -ForegroundColor Green
    } catch {
        Write-Host "[WARN] LFS setup failed - skipping" -ForegroundColor Yellow
    }
} else {
    Write-Host "[SKIP] LFS not installed" -ForegroundColor Gray
}

Write-Host ""
Write-Host "--- Step 4: Verify Library is ignored ---" -ForegroundColor Cyan

$libIgnoreCheck = git check-ignore Library 2>&1
if ($libIgnoreCheck -match "Library") {
    Write-Host "[OK] Library/ is properly ignored" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Library/ is NOT ignored! Check .gitignore" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host ""
Write-Host "--- Step 5: Stage and Commit ---" -ForegroundColor Cyan
Write-Host "   (This may take 1-2 minutes due to many files)" -ForegroundColor Gray

git add .gitattributes 2>&1 | Out-Null
git add .gitignore 2>&1 | Out-Null
git commit -m "chore: setup Git LFS and gitignore" 2>&1 | Out-Null
Write-Host "[OK] Commit 1: gitignore and LFS setup" -ForegroundColor Green

git add . 2>&1 | Out-Null
git commit -m "Initial commit: Startup City Unity project with OTP signup" 2>&1 | Out-Null
Write-Host "[OK] Commit 2: source code" -ForegroundColor Green

Write-Host ""
Write-Host "--- Step 6: Scan for files over 95MB ---" -ForegroundColor Cyan

$bigFiles = git rev-list --objects --all 2>&1 |
    ForEach-Object {
        $parts = $_ -split ' ', 2
        if ($parts.Count -eq 2) {
            $hash = $parts[0]
            $name = $parts[1]
            $size = git cat-file -s $hash 2>&1
            if ($size -match '^\d+$' -and [int64]$size -gt 95MB) {
                [PSCustomObject]@{
                    SizeMB = [math]::Round([int64]$size / 1MB, 2)
                    File = $name
                }
            }
        }
    } | Sort-Object SizeMB -Descending | Select-Object -First 5

if ($bigFiles) {
    Write-Host "[WARN] Found large files - push may fail:" -ForegroundColor Yellow
    $bigFiles | Format-Table -AutoSize
} else {
    Write-Host "[OK] No files over 95MB - ready to push!" -ForegroundColor Green
}

Write-Host ""
Write-Host "===============================================" -ForegroundColor Green
Write-Host "  DONE! " -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Open GitHub Desktop"
Write-Host "  2. It should detect this repo automatically"
Write-Host "     (if not: File - Add Local Repository - choose this folder)"
Write-Host "  3. Click 'Publish repository' at the top right"
Write-Host "  4. Tick 'Keep this code private' then Publish"
Write-Host ""
Write-Host "*** Delete old repo on GitHub.com first if any! ***" -ForegroundColor Yellow
Write-Host ""

Read-Host "Press Enter to close"
