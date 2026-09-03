$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) {
    throw 'Missing .env.'
}

$values = @{}
foreach ($line in Get-Content -LiteralPath $envFile) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    $pair = $line.Split('=', 2)
    if ($pair.Count -eq 2) { $values[$pair[0]] = $pair[1] }
}

$port = if ($values.APP_PORT) { $values.APP_PORT } else { '8085' }
$baseUrl = if ($env:DEMO_BASE_URL) { $env:DEMO_BASE_URL.TrimEnd('/') } else { "http://localhost:$port" }
$body = @{ confirmation = 'RESET_DEMO' } | ConvertTo-Json

Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/demo/reset" `
    -Headers @{ 'X-Demo-Admin-Token' = $values.DEMO_ADMIN_TOKEN } `
    -ContentType 'application/json' `
    -Body $body
