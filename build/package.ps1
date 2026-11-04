<#
.SYNOPSIS
  Builds the Windows release zips: self-contained and framework-dependent, each with SolidWorksMcp.exe,
  INSTALL.md, README.md, LICENSE.txt, THIRD-PARTY-NOTICES.md and an example Claude Desktop config. Writes SHA256SUMS.txt next to them.
  With -SigningCertPath, SolidWorksMcp.exe is Authenticode-signed (SHA-256, RFC 3161 timestamp) before zipping,
  and the public certificate is added to each zip as SolidWorksMcp-signing.cer. The .pfx password is read from the
  SIGNING_CERT_PASSWORD environment variable so it never appears on a command line or in logs.
.EXAMPLE
  pwsh build/package.ps1 -Version 0.1.0
  $env:SIGNING_CERT_PASSWORD = '...'; pwsh build/package.ps1 -Version 0.1.0 -SigningCertPath signing/solidworks-mcp-signing.pfx
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutDir = "artifacts",
    # .pfx with a code-signing certificate and its private key. Omit to build unsigned.
    [string]$SigningCertPath,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    # CI only: trust the (self-signed) certificate on this machine so the signature can be verified as Valid.
    [switch]$TrustOnThisMachine
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SolidWorksMcp/SolidWorksMcp.csproj'
$out = Join-Path $root $OutDir
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $out | Out-Null

$cert = $null
if ($SigningCertPath) {
    if (-not (Test-Path $SigningCertPath)) { throw "Signing certificate '$SigningCertPath' not found." }
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        (Resolve-Path $SigningCertPath).Path, $env:SIGNING_CERT_PASSWORD, $flags)
    if (-not $cert.HasPrivateKey) { throw 'The signing .pfx has no private key.' }
    $codeSigning = $cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }
    if (-not $codeSigning) { throw 'The signing certificate is not a code-signing certificate (EKU 1.3.6.1.5.5.7.3.3 missing).' }
    if ($cert.NotAfter -lt (Get-Date)) { throw "The signing certificate expired on $($cert.NotAfter.ToString('yyyy-MM-dd'))." }
    if ($TrustOnThisMachine) {
        $public = Join-Path ([IO.Path]::GetTempPath()) 'swmcp-signing.cer'
        [IO.File]::WriteAllBytes($public, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Import-Certificate -FilePath $public -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath $public -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
    }
    Write-Host "Signing with '$($cert.Subject)', thumbprint $($cert.Thumbprint), valid to $($cert.NotAfter.ToString('yyyy-MM-dd'))."
}
else {
    Write-Warning 'No -SigningCertPath: building UNSIGNED executables.'
}

function Invoke-Sign([string]$exe) {
    $result = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    $sig = Get-AuthenticodeSignature -FilePath $exe
    if ($sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
        throw "Signing $exe failed: $($result.StatusMessage)"
    }
    if (-not $sig.TimeStamperCertificate) { throw "Signing $exe produced no timestamp (server $TimestampServer)." }
    if ($TrustOnThisMachine -and $sig.Status -ne 'Valid') { throw "Signature on $exe is $($sig.Status): $($sig.StatusMessage)" }
    Write-Host "Signed $exe ($($sig.Status), timestamped by $($sig.TimeStamperCertificate.Subject))."
}

$variants = @(
    @{ Name = "solidworks-mcp-$Version-win-x64";           SelfContained = 'true'  },
    @{ Name = "solidworks-mcp-$Version-win-x64-framework"; SelfContained = 'false' }
)
foreach ($v in $variants) {
    $stage = Join-Path $out $v.Name
    $publishArgs = @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', $v.SelfContained,
        '-p:PublishSingleFile=true', '-p:DebugType=embedded',
        "-p:Version=$Version", '-o', $stage)
    if ($v.SelfContained -eq 'true') { $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true' }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($v.Name)" }
    if ($cert) {
        Invoke-Sign (Join-Path $stage 'SolidWorksMcp.exe')
        [IO.File]::WriteAllBytes((Join-Path $stage 'SolidWorksMcp-signing.cer'),
            $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    }

    Copy-Item (Join-Path $root 'docs/INSTALL.md') $stage
    Copy-Item (Join-Path $root 'README.md') $stage
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
    Copy-Item (Join-Path $root 'examples/claude_desktop_config.json') (Join-Path $stage 'claude_desktop_config.example.json')

    $zip = Join-Path $out "$($v.Name).zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
}

Get-ChildItem $out -Filter *.zip | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
if ($cert) {
    [IO.File]::WriteAllBytes((Join-Path $out 'SolidWorksMcp-signing.cer'),
        $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $cert.Thumbprint | Set-Content (Join-Path $out 'signing-thumbprint.txt') -Encoding ascii -NoNewline
}
Get-ChildItem $out -File | Format-Table Name, Length
