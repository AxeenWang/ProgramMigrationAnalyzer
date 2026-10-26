# Passwords are SecureString values or read interactively. Never accept plaintext password arguments.
[CmdletBinding()]
param(
    [string]$Username,
    [Security.SecureString]$Password,
    [Security.SecureString]$Confirmation,
    [string]$OutputPath,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $sourceRoot 'config/embedded-login.json' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (!$OutputPath.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Credential configuration must remain inside the project root.'
}
if ((Test-Path -LiteralPath $OutputPath) -and ((Get-Item -LiteralPath $OutputPath).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Reparse credential destinations are not supported.'
}
for ($ancestor = [IO.DirectoryInfo][IO.Path]::GetDirectoryName($OutputPath); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if ($ancestor.Exists -and (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'Reparse credential destinations are not supported.'
    }
}
if ((Test-Path -LiteralPath $OutputPath) -and !$Force) { throw 'Configuration already exists. Use -Force only when deliberately changing credentials.' }
if ([string]::IsNullOrEmpty($Username)) { $Username = Read-Host 'Account' }
if ([string]::IsNullOrWhiteSpace($Username) -or $Username.Length -gt 128) { throw 'Account must contain 1 to 128 characters.' }
if ($null -eq $Password) { $Password = Read-Host 'Password' -AsSecureString }
if ($null -eq $Confirmation) { $Confirmation = Read-Host 'Confirm password' -AsSecureString }

function ConvertTo-PasswordBytes([Security.SecureString]$Value) {
    if ($Value.Length -lt 1 -or $Value.Length -gt 128) { throw 'Password must contain 1 to 128 characters.' }
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($Value)
    $characters = New-Object char[] $Value.Length
    try {
        [Runtime.InteropServices.Marshal]::Copy($pointer, $characters, 0, $characters.Length)
        return ,([Text.UTF8Encoding]::new($false, $true).GetBytes($characters))
    } finally {
        [Array]::Clear($characters, 0, $characters.Length)
        [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($pointer)
    }
}

$passwordBytes = $null
$confirmationBytes = $null
$derived = $null
$rng = $null
$kdf = $null
try {
    $passwordBytes = ConvertTo-PasswordBytes $Password
    $confirmationBytes = ConvertTo-PasswordBytes $Confirmation
    if ($passwordBytes.Length -ne $confirmationBytes.Length) { throw 'Passwords do not match.' }
    $difference = 0
    for ($index = 0; $index -lt $passwordBytes.Length; $index++) {
        $difference = $difference -bor ($passwordBytes[$index] -bxor $confirmationBytes[$index])
    }
    if ($difference -ne 0) { throw 'Passwords do not match.' }
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt)
    $kdf = [Security.Cryptography.Rfc2898DeriveBytes]::new($passwordBytes, $salt, 600000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $derived = $kdf.GetBytes(32)
    $json = [ordered]@{
        schemaVersion = 1
        username = $Username
        algorithm = 'PBKDF2-HMAC-SHA256'
        iterations = 600000
        salt = [Convert]::ToBase64String($salt)
        hash = [Convert]::ToBase64String($derived)
    } | ConvertTo-Json
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
    [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
    Write-Host 'Embedded login verifier saved. No plaintext password was written. Rebuild to apply it.'
} finally {
    foreach ($bytes in @($passwordBytes, $confirmationBytes, $derived)) {
        if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
    }
    if ($null -ne $kdf) { $kdf.Dispose() }
    if ($null -ne $rng) { $rng.Dispose() }
    if ($null -ne $Password) { $Password.Dispose() }
    if ($null -ne $Confirmation) { $Confirmation.Dispose() }
}
