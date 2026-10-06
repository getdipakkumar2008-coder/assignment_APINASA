# Verifies the NASA key (from the git-ignored .env) is absent from every tracked file and from git history.
# Prints only "clean" or the names of offending files / commits - never the key itself.
$ErrorActionPreference = 'Continue'
Set-Location (git rev-parse --show-toplevel)
$bad = $false
$key = $null
if (Test-Path .env) {
    $line = Get-Content .env | Where-Object { $_ -match '^NASA__ApiKey=' } | Select-Object -First 1
    if ($line) { $key = ($line -replace '^NASA__ApiKey=', '').Trim() }
}
if ($key) {
    $files = git grep -lF -e $key -- . 2>$null
    if ($files) { Write-Output 'KEY FOUND in tracked files:'; $files; $bad = $true }
    $commits = git log --all --no-textconv --format=%h "-S$key" 2>$null
    if ($commits) { Write-Output 'KEY FOUND in history, commits:'; $commits; $bad = $true }
} else {
    Write-Output 'no key found in .env; scanning for key-like assignments only'
}
$tracked = git ls-files --error-unmatch .env 2>$null
if ($LASTEXITCODE -eq 0 -and $tracked) { Write-Output '.env is TRACKED'; $bad = $true }
if (-not $bad) { Write-Output 'clean' }
if ($bad) { exit 1 }
