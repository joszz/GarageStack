<#
.SYNOPSIS
GarageStack demo launcher.
Always starts API + Frontend. Also opens an interactive menu pane on the left.
Without -Menu : sets up env and opens Windows Terminal with all three panes.
With    -Menu : runs the menu pane (called internally by Windows Terminal).
#>
param([switch]$Menu)

$ErrorActionPreference = 'Stop'
$root         = $PSScriptRoot
$apiPath      = Join-Path $root 'src\GarageStack.Api'
$frontendPath = Join-Path $root 'frontend'

function Test-PortOpen([int]$port) {
    $tcp = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $tcp.ConnectAsync('127.0.0.1', $port)
        if ($task.Wait(200)) { return $tcp.Connected }
        return $false
    } catch { return $false }
    finally { $tcp.Dispose() }
}

function Stop-ProcessOnPort([int]$port) {
    $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($conn) {
        Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
    }
}

function Write-Dot([int]$port) {
    if (Test-PortOpen $port) {
        Write-Host ' [+] ' -NoNewline -ForegroundColor Green
    } else {
        Write-Host ' [ ] ' -NoNewline -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------
# Menu mode - runs inside the left pane
# ---------------------------------------------------------------------------
if ($Menu) {
    while ($true) {
        Clear-Host
        Write-Host ''
        Write-Host '  +----------------------------+' -ForegroundColor Yellow
        Write-Host '  |     GarageStack Demo       |' -ForegroundColor Yellow
        Write-Host '  +----------------------------+' -ForegroundColor Yellow
        Write-Host ''
        Write-Host '  Status' -ForegroundColor DarkGray
        Write-Host '  API      ' -ForegroundColor Cyan -NoNewline
        Write-Dot 5000
        Write-Host 'localhost:5000' -ForegroundColor DarkGray
        Write-Host '  Frontend ' -ForegroundColor Cyan -NoNewline
        Write-Dot 5173
        Write-Host 'localhost:5173' -ForegroundColor DarkGray
        Write-Host ''
        Write-Host '  Open in browser' -ForegroundColor DarkGray
        Write-Host '  [s]  Scalar API docs' -ForegroundColor Green
        Write-Host '  [a]  App  (demo / demo)' -ForegroundColor Green
        Write-Host '  [o]  OpenAPI JSON' -ForegroundColor Green
        Write-Host ''
        Write-Host '  [v]  Open in VS Code' -ForegroundColor DarkGray
        Write-Host '  [r]  Refresh status' -ForegroundColor DarkGray
        Write-Host '  [q]  Quit' -ForegroundColor Red
        Write-Host ''
        Write-Host '  > ' -NoNewline -ForegroundColor Yellow

        $key = $host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')

        switch ($key.Character) {
            's' { Start-Process 'http://localhost:5000/scalar/v1' }
            'a' { Start-Process 'http://localhost:5173' }
            'o' { Start-Process 'http://localhost:5000/openapi/v1.json' }
            'v' { code $root }
            'r' {}
            'q' {
                Stop-ProcessOnPort 5000
                Stop-ProcessOnPort 5173
                exit 0
            }
        }
    }

# ---------------------------------------------------------------------------
# Launcher mode - env setup, then open WT with all three panes
# ---------------------------------------------------------------------------
} else {
    $envLocal        = Join-Path $frontendPath '.env.development.local'
    $envLocalExample = Join-Path $frontendPath '.env.development.local.example'

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
        Write-Warning 'Windows Terminal (wt) not found -- opening separate windows instead.'
        Start-Process pwsh -ArgumentList '-NoExit', '-Command', "cd '$apiPath'; dotnet run --launch-profile Demo"
        Start-Process pwsh -ArgumentList '-NoExit', '-Command', "cd '$frontendPath'; pnpm dev"
        & $PSCommandPath -Menu
        exit 0
    }

    $scriptPath = $PSCommandPath
    $cmd = "& `"$scriptPath`" -Menu"

    # Layout: [Menu (left) | API (top-right) / Frontend (bottom-right)]
    # 1. new-tab  -> menu pane (full width)
    # 2. split -V -> menu stays left (28%), API opens right (72%), focus moves to API
    # 3. split -H -> API pane splits top/bottom for Frontend
    $wtArgs = @(
        'new-tab', '--title', 'GarageStack', '--tabColor', '#10b981',
        '-d', $root, 'pwsh', '-NoExit', '-Command', $cmd,
        ';',
        'split-pane', '-V', '--size', '0.72',
        '--title', 'GarageStack API (Demo)', '--tabColor', '#f59e0b',
        '-d', $apiPath, 'pwsh', '-NoExit', '-Command', 'dotnet run --launch-profile Demo',
        ';',
        'split-pane', '-H',
        '--title', 'GarageStack Frontend (Demo)', '--tabColor', '#3b82f6',
        '-d', $frontendPath, 'pwsh', '-NoExit', '-Command', 'pnpm dev',
        ';',
        'focus-pane', '--target', '0'
    )

    & wt @wtArgs
}
