<#
.SYNOPSIS
  Creates the self-signed code-signing certificate for SolidWorks MCP releases (internal use only).

.DESCRIPTION
  Run once on a trusted Windows PC. It writes into -OutDir:
    solidworks-mcp-signing.pfx      private key + certificate, protected by the password you type. Keep it secret.
    solidworks-mcp-signing.pfx.b64  the same .pfx as base64, to paste into the SIGNING_CERT_PFX_BASE64 GitHub secret.
    solidworks-mcp-signing.cer      public certificate only. IT deploys this to Trusted Root + Trusted Publishers.
  The certificate is then removed from the Windows certificate store, so the .pfx is the only copy of the key.

.EXAMPLE
  pwsh build/new-signing-cert.ps1
  pwsh build/new-signing-cert.ps1 -Subject "CN=Samer William Al-Haddadin, O=My Company, C=JO" -Years 3
#>
param(
    [string]$Subject = 'CN=Samer William Al-Haddadin',
    [ValidateRange(1, 10)][int]$Years = 5,
    [string]$OutDir = 'signing'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'Run this on Windows: New-SelfSignedCertificate is Windows-only.' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$pfx = Join-Path $OutDir 'solidworks-mcp-signing.pfx'
$cer = Join-Path $OutDir 'solidworks-mcp-signing.cer'
if (Test-Path $pfx) { throw "$pfx already exists. Move it away first; overwriting would lose the existing key." }

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears($Years) `
    -FriendlyName 'SolidWorks MCP code signing'
try {
    $pw = Read-Host -AsSecureString 'Password for the .pfx (store it as the SIGNING_CERT_PASSWORD secret)'
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)) | Set-Content "$pfx.b64" -Encoding ascii -NoNewline
}
finally {
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Subject:    $($cert.Subject)"
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Valid to:   $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ''
Write-Host 'Next:'
Write-Host "  1. GitHub > Settings > Secrets and variables > Actions > New repository secret:"
Write-Host "       SIGNING_CERT_PFX_BASE64 = contents of $pfx.b64"
Write-Host "       SIGNING_CERT_PASSWORD   = the password you just typed"
Write-Host "  2. Delete $pfx.b64, and keep $pfx offline (password manager or encrypted drive)."
Write-Host "  3. Give $cer and the thumbprint above to IT (see INSTALL.md, 'Trusting the signature')."
