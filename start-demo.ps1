<#
.SYNOPSIS
GarageStack demo launcher.
Always starts API + Frontend. Also opens an interactive menu pane on the left.
Without -Menu : sets up env and opens Windows Terminal with all three panes.
With    -Menu : runs the menu pane (called internally by Windows Terminal).
With -Frontend: waits for the API port, then runs Vite (so the first page
                load does not hit ECONNREFUSED while dotnet is still building).
With    -Api  : runs the API with the Demo launch profile and the settings from .env.demo.

The demo settings live in one place, .env.demo, for this script and for docker-compose.demo.yml
alike. The script creates it from .env.demo.example on first run. Its ports and CORS origin
are the Docker stack's: here the API is on :5000 and Vite on :5173.
#>
param([switch]$Menu, [switch]$Frontend, [switch]$Api)

$ErrorActionPreference = 'Stop'
$root         = $PSScriptRoot
$apiPath      = Join-Path $root 'src\GarageStack.Api'
$frontendPath = Join-Path $root 'frontend'
$demoEnv      = Join-Path $root '.env.demo'
$demoEnvExample = Join-Path $root '.env.demo.example'

# Settings in .env.demo that only the Docker stack reads: its host ports, its origin, and the
# switch it always sets itself.
$dockerOnly = @('FRONTEND_PORT', 'API_PORT', 'CORS_ORIGIN', 'DEMO_MODE')

# The KEY=VALUE lines of .env.demo (or the example while there is none), without comments,
# blank values or surrounding quotes. A blank value means "unset", as in the compose file.
function Read-DemoSettings {
    $path = if (Test-Path $demoEnv) { $demoEnv } else { $demoEnvExample }
    $settings = [ordered]@{}
    if (-not (Test-Path $path)) { return $settings }
    foreach ($line in Get-Content $path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        $at = $trimmed.IndexOf('=')
        if ($at -lt 1) { continue }
        $name  = $trimmed.Substring(0, $at).Trim()
        $value = $trimmed.Substring($at + 1).Trim()
        if ($value.Length -ge 2 -and $value[0] -eq $value[-1] -and $value[0] -in '"', "'") {
            $value = $value.Substring(1, $value.Length - 2)
        }
        if ($value) { $settings[$name] = $value }
    }
    return $settings
}

function Get-DemoLogin {
    $settings = Read-DemoSettings
    $user = if ($settings['AUTH_USERNAME']) { $settings['AUTH_USERNAME'] } else { 'demo' }
    $pass = if ($settings['AUTH_PASSWORD']) { $settings['AUTH_PASSWORD'] } else { 'demo' }
    return "$user / $pass"
}

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

function Stop-WtParent {
    $currentPid = $PID
    while ($currentPid) {
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId = $currentPid" -ErrorAction SilentlyContinue
        if (-not $proc) { break }
        $parent = Get-Process -Id $proc.ParentProcessId -ErrorAction SilentlyContinue
        if ($parent -and $parent.Name -eq 'WindowsTerminal') {
            Stop-Process -Id $parent.Id -Force -ErrorAction SilentlyContinue
            break
        }
        $currentPid = $proc.ParentProcessId
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
    $login = Get-DemoLogin
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
        Write-Host "  [a]  App  ($login)" -ForegroundColor Green
        Write-Host '  [s]  API reference (Scalar)' -ForegroundColor Green
        Write-Host '  [o]  OpenAPI JSON' -ForegroundColor Green
        Write-Host ''
        Write-Host '  [v]  Open in VS Code' -ForegroundColor DarkGray
        Write-Host '  [r]  Refresh status' -ForegroundColor DarkGray
        Write-Host '  [q]  Quit' -ForegroundColor Red
        Write-Host ''
        Write-Host '  > ' -NoNewline -ForegroundColor Yellow

        $key = $host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')

        switch ($key.Character) {
            # Through Vite, on the app's own address: the session cookie and Origin are the app's,
            # so Test Request works once signed in (the reference sends you to sign in first).
            's' { Start-Process 'http://localhost:5173/api/reference/' }
            'a' { Start-Process 'http://localhost:5173' }
            'o' { Start-Process 'http://localhost:5173/api/openapi/v1.json' }
            'v' { code $root }
            'r' {}
            'q' {
                Stop-ProcessOnPort 5000
                Stop-ProcessOnPort 5173
                Stop-WtParent
                exit 0
            }
        }
    }

# ---------------------------------------------------------------------------
# Frontend mode - waits for the API, then starts Vite
# ---------------------------------------------------------------------------
} elseif ($Frontend) {
    Set-Location $frontendPath
    Write-Host 'Waiting for the API on localhost:5000 ...' -ForegroundColor DarkGray
    $deadline = (Get-Date).AddMinutes(3)
    while (-not (Test-PortOpen 5000)) {
        if ((Get-Date) -gt $deadline) {
            Write-Warning 'API did not come up within 3 minutes, starting Vite anyway.'
            break
        }
        Start-Sleep -Milliseconds 500
    }
    pnpm dev

# ---------------------------------------------------------------------------
# API mode - the Demo launch profile, with the settings from .env.demo
# ---------------------------------------------------------------------------
} elseif ($Api) {
    Set-Location $apiPath
    # dotnet run -e beats the launch profile's own values, so .env.demo has the last word.
    $runArgs = @('run', '--launch-profile', 'Demo')
    $settings = Read-DemoSettings
    foreach ($name in $settings.Keys) {
        if ($name -in $dockerOnly) { continue }
        $runArgs += '-e', "$name=$($settings[$name])"
        if ($name -ne 'AUTH_PASSWORD') {
            Write-Host "  $name=$($settings[$name])" -ForegroundColor DarkGray
        }
    }
    dotnet @runArgs

# ---------------------------------------------------------------------------
# Launcher mode - env setup, then open WT with all three panes
# ---------------------------------------------------------------------------
} else {
    if (-not (Test-Path $demoEnv) -and (Test-Path $demoEnvExample)) {
        Copy-Item $demoEnvExample $demoEnv
        Write-Host 'Created .env.demo from example' -ForegroundColor Green
    }

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
    Write-Host "  Frontend -> http://localhost:5173  (login: $(Get-DemoLogin))" -ForegroundColor Cyan
    Write-Host ''

    if (-not (Get-Command wt -ErrorAction SilentlyContinue)) {
        Write-Warning 'Windows Terminal (wt) not found -- opening separate windows instead.'
        Start-Process pwsh -ArgumentList '-NoExit', '-Command', "& '$PSCommandPath' -Api"
        Start-Process pwsh -ArgumentList '-NoExit', '-Command', "& '$PSCommandPath' -Frontend"
        & $PSCommandPath -Menu
        exit 0
    }

    $scriptPath = $PSCommandPath
    $cmd = "& `"$scriptPath`" -Menu"
    $frontendCmd = "& `"$scriptPath`" -Frontend"
    $apiCmd = "& `"$scriptPath`" -Api"

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
        '-d', $apiPath, 'pwsh', '-NoExit', '-Command', $apiCmd,
        ';',
        'split-pane', '-H',
        '--title', 'GarageStack Frontend (Demo)', '--tabColor', '#3b82f6',
        '-d', $frontendPath, 'pwsh', '-NoExit', '-Command', $frontendCmd,
        ';',
        'focus-pane', '--target', '0'
    )

    & wt @wtArgs
}
