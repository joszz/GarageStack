# Launches GarageStack demo mode in Windows Terminal with two horizontal panes.
# Top pane: .NET API (demo launch profile)  http://localhost:5000
# Bottom pane: Vite frontend (demo mode)     http://localhost:5173

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$apiPath = Join-Path $root 'src\GarageStack.Api'
$frontendPath = Join-Path $root 'frontend'
$envLocal = Join-Path $frontendPath '.env.development.local'
$envLocalExample = Join-Path $frontendPath '.env.development.local.example'

# Auto-create frontend/.env.development.local if absent
if (-not (Test-Path $envLocal)) {
    if (Test-Path $envLocalExample) {
        Copy-Item $envLocalExample $envLocal
        Write-Host 'Created frontend/.env.development.local from example' -ForegroundColor Green
    } else {
        'VITE_DEMO_MODE=true' | Set-Content $envLocal -Encoding UTF8
        Write-Host 'Created frontend/.env.development.local' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host '  GarageStack Demo' -ForegroundColor Yellow
Write-Host '  API      -> http://localhost:5000' -ForegroundColor Cyan
Write-Host '  Frontend -> http://localhost:5173  (login: demo / demo)' -ForegroundColor Cyan
Write-Host ''

if (-not (Get-Command wt -ErrorAction SilentlyContinue)) {
    Write-Warning 'Windows Terminal (wt) not found -- opening two separate windows instead.'
    Start-Process pwsh -ArgumentList '-NoExit', '-Command', "cd '$apiPath'; dotnet run --launch-profile Demo"
    Start-Process pwsh -ArgumentList '-NoExit', '-Command', "cd '$frontendPath'; pnpm dev"
    exit 0
}

$wtArgs = @(
    'new-tab', '--title', 'GarageStack API (Demo)', '--tabColor', '#f59e0b',
    '-d', $apiPath,
    'pwsh', '-NoExit', '-Command', 'dotnet run --launch-profile Demo',
    ';',
    'split-pane', '-H', '--title', 'GarageStack Frontend (Demo)', '--tabColor', '#3b82f6',
    '-d', $frontendPath,
    'pwsh', '-NoExit', '-Command', 'pnpm dev'
)

& wt @wtArgs
