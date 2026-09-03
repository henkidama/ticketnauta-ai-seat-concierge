$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) {
    throw 'Missing .env. Copy .env.example to .env and fill in local values.'
}

foreach ($line in Get-Content -LiteralPath $envFile) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    $pair = $line.Split('=', 2)
    if ($pair.Count -eq 2) { Set-Item -Path "Env:$($pair[0])" -Value $pair[1] }
}

$env:ConnectionStrings__DemoDatabase = "Host=$env:DB_HOST;Port=$env:DB_PORT;Database=$env:DB_NAME;Username=$env:DB_USER;Password=$env:DB_PASSWORD;SSL Mode=$env:DB_SSL_MODE;Timeout=10;Command Timeout=30;Pooling=true"
$env:Demo__AdminToken = $env:DEMO_ADMIN_TOKEN
$env:Demo__HoldMinutes = $env:HOLD_MINUTES
$env:ASPNETCORE_URLS = "http://localhost:$env:APP_PORT"

dotnet run --project (Join-Path $repoRoot 'src/Ticketnauta.WebMcp.Api/Ticketnauta.WebMcp.Api.csproj')
