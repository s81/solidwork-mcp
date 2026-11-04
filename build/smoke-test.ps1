<#
.SYNOPSIS
  Starts a published SolidWorksMcp.exe, performs the MCP handshake over stdio and checks the tool list.
  Does not need SolidWorks.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$MinTools = 84,
    [string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
try {
    $p.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1"}}}')
    $p.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $p.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
    $p.StandardInput.Flush()

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $tools = $null
    while (-not $tools -and [DateTime]::UtcNow -lt $deadline) {
        $read = $p.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(60000) -or $null -eq $read.Result) { break }
        $msg = $read.Result | ConvertFrom-Json
        if ($msg.id -eq 1) {
            Write-Host "initialize: $($msg.result.serverInfo.name) $($msg.result.serverInfo.version)"
            if ($ExpectedVersion -and $msg.result.serverInfo.version -ne $ExpectedVersion) {
                throw "Server reports version $($msg.result.serverInfo.version), expected $ExpectedVersion."
            }
        }
        if ($msg.id -eq 2) { $tools = $msg.result.tools }
    }
    if (-not $tools) { throw "No tools/list response from $Exe. stderr: $($p.StandardError.ReadToEnd())" }
    Write-Host "tools/list: $($tools.Count) tools"
    if ($tools.Count -lt $MinTools) { throw "Expected at least $MinTools tools, got $($tools.Count)." }
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
}
