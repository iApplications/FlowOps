param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Api', 'Studio', 'Portal')]
    [string]$Service
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$httpsPath = Join-Path $env:LOCALAPPDATA 'FlowOps\Https'
$certPath = Join-Path $httpsPath 'localhost.pem'
$keyPath = Join-Path $httpsPath 'localhost.key'
if (!(Test-Path -LiteralPath $certPath) -or !(Test-Path -LiteralPath $keyPath)) {
    throw 'Create and trust the local HTTPS certificate first. See docs/DEVELOPMENT.md.'
}

$env:StudioUrl = 'https://localhost:3443'
$env:PortalUrl = 'https://localhost:3444'
$env:FLOWOPS_API_URL = 'https://localhost:5443'
$env:NODE_EXTRA_CA_CERTS = $certPath
$env:FLOWOPS_HTTPS_TEST = '1'
Push-Location $repoPath
try {
    if ($Service -eq 'Api') {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:Kestrel__Certificates__Default__Path = $certPath
        $env:Kestrel__Certificates__Default__KeyPath = $keyPath
        & "$env:USERPROFILE\.dotnet\dotnet.exe" run --project backend/FlowOps.Api -c Release --no-launch-profile --urls https://localhost:5443
    } else {
        $workspace = if ($Service -eq 'Studio') { 'studio-web' } else { 'portal-web' }
        $port = if ($Service -eq 'Studio') { '3443' } else { '3444' }
        & npm.cmd run dev --workspace $workspace -- --hostname 127.0.0.1 --port $port --experimental-https --experimental-https-key $keyPath --experimental-https-cert $certPath --experimental-https-ca $certPath
    }
    if ($LASTEXITCODE -ne 0) { throw "$Service exited with code $LASTEXITCODE." }
} finally { Pop-Location }
