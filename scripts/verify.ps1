$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet restore .\Ticketnauta.WebMcp.slnx --configfile .\NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet build .\Ticketnauta.WebMcp.slnx --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    dotnet test .\Ticketnauta.WebMcp.slnx --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    npm run check:web
    if ($LASTEXITCODE -ne 0) { throw 'Web contract checks failed.' }
}
finally {
    Pop-Location
}
