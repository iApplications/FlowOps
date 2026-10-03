$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Security

$flowopsPassword = Read-Host 'FlowOps database password' -AsSecureString
$flowopsPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($flowopsPassword)
try {
    $flowopsPlaintext = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($flowopsPointer)
    $flowopsBytes = [Text.Encoding]::UTF8.GetBytes($flowopsPlaintext)
    $flowopsEncrypted = [Security.Cryptography.ProtectedData]::Protect(
        $flowopsBytes,
        $null,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)

    $flowopsDirectory = Join-Path $env:LOCALAPPDATA 'FlowOps\Secrets'
    New-Item -ItemType Directory -Path $flowopsDirectory -Force | Out-Null
    [IO.File]::WriteAllBytes(
        (Join-Path $flowopsDirectory 'Development.dbpassword.dpapi'),
        $flowopsEncrypted)

    [Array]::Clear($flowopsBytes, 0, $flowopsBytes.Length)
    Write-Output 'Encrypted FlowOps database password saved for this Windows account.'
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($flowopsPointer)
}
