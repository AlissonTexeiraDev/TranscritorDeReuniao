<#
.SYNOPSIS
  Publica o cliente de bandeja (TranscritorDeReuniao.exe) e o servidor MCP (MeetingNotesMcp.exe)
  em uma única pasta pronta para instalar ou copiar.

.PARAMETER Out
  Pasta de destino. Padrão: <repositório>\publish\TranscritorDeReuniao

.PARAMETER FrameworkDependent
  Gera uma publicação menor que depende do runtime .NET 10 (Desktop) já instalado na máquina.
  Por padrão a publicação é self-contained: não exige .NET instalado no PC de destino.

.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -Out C:\Ferramentas\TranscritorDeReuniao -FrameworkDependent
#>
param(
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish\TranscritorDeReuniao'),
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }

New-Item -ItemType Directory -Force -Path $Out | Out-Null

foreach ($proj in 'src\MeetingNotes.Client\MeetingNotes.Client.csproj', 'src\MeetingNotes.Mcp\MeetingNotes.Mcp.csproj') {
    Write-Host "Publicando $proj ..." -ForegroundColor Cyan
    dotnet publish (Join-Path $root $proj) -c Release -r win-x64 --self-contained $selfContained -o $Out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou para $proj" }
}

Copy-Item (Join-Path $PSScriptRoot 'install.ps1') (Join-Path $Out 'install.ps1') -Force

Write-Host ""
Write-Host "Publicado em: $Out" -ForegroundColor Green
Write-Host "  Cliente de bandeja : $Out\TranscritorDeReuniao.exe"
Write-Host "  Servidor MCP       : $Out\MeetingNotesMcp.exe"
Write-Host "Para instalar no PC: .\scripts\install.ps1 [-Startup]"
