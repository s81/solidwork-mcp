<#
.SYNOPSIS
  Builds the Windows release zips: self-contained and framework-dependent, each with SolidWorksMcp.exe,
  INSTALL.md, README.md and an example Claude Desktop config. Writes SHA256SUMS.txt next to them.
.EXAMPLE
  pwsh build/package.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutDir = "artifacts"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SolidWorksMcp/SolidWorksMcp.csproj'
$out = Join-Path $root $OutDir
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $out | Out-Null

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

    Copy-Item (Join-Path $root 'docs/INSTALL.md') $stage
    Copy-Item (Join-Path $root 'README.md') $stage
    Copy-Item (Join-Path $root 'examples/claude_desktop_config.json') (Join-Path $stage 'claude_desktop_config.example.json')

    $zip = Join-Path $out "$($v.Name).zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
}

Get-ChildItem $out -Filter *.zip | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem $out -File | Format-Table Name, Length
