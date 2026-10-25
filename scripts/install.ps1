<#
.SYNOPSIS
  Instala (ou desinstala) o Transcritor de Reunião para o usuário atual, sem precisar de administrador.

.DESCRIPTION
  Copia a pasta publicada para %LOCALAPPDATA%\Programs\TranscritorDeReuniao, cria o atalho no Menu Iniciar,
  registra o programa em "Aplicativos instalados" do Windows e, com -Startup, faz o cliente iniciar com o Windows
  minimizado na bandeja. Se a pasta publicada não existir, roda publish.ps1 antes.

.PARAMETER Source
  Pasta publicada. Padrão: <repositório>\publish\TranscritorDeReuniao (ou a própria pasta do script, quando
  rodado de dentro de uma publicação).

.PARAMETER Startup
  Registrar para iniciar com o Windows.

.PARAMETER NoLaunch
  Não abrir o cliente ao terminar.

.PARAMETER Uninstall
  Remove a instalação, o atalho, o registro em "Aplicativos instalados" e a inicialização com o Windows.

.EXAMPLE
  .\scripts\install.ps1 -Startup
  .\scripts\install.ps1 -Uninstall
#>
param(
    [string]$Source,
    [switch]$Startup,
    [switch]$NoLaunch,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$appName = 'Transcritor de Reunião'
$appId = 'TranscritorDeReuniao'
$version = '0.2.0'
$installDir = Join-Path $env:LOCALAPPDATA "Programs\$appId"
$exe = Join-Path $installDir "$appId.exe"
$mcpExe = Join-Path $installDir 'MeetingNotesMcp.exe'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Stop-Client {
    Get-Process -Name $appId -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

if ($Uninstall) {
    Stop-Client
    if (Test-Path $installDir) { Remove-Item -Recurse -Force $installDir }
    if (Test-Path $startMenuLink) { Remove-Item -Force $startMenuLink }
    if (Test-Path $uninstallKey) { Remove-Item -Recurse -Force $uninstallKey }
    Remove-ItemProperty -Path $runKey -Name $appId -ErrorAction SilentlyContinue
    Write-Host "$appName desinstalado." -ForegroundColor Green
    Write-Host "As gravações e as configurações (%APPDATA%\$appId) foram mantidas."
    return
}

if (-not $Source) {
    if (Test-Path (Join-Path $PSScriptRoot "$appId.exe")) {
        $Source = $PSScriptRoot
    } else {
        $Source = Join-Path (Split-Path $PSScriptRoot -Parent) "publish\$appId"
    }
}

if (-not (Test-Path (Join-Path $Source "$appId.exe"))) {
    $publish = Join-Path $PSScriptRoot 'publish.ps1'
    if (-not (Test-Path $publish)) { throw "Pasta publicada não encontrada em '$Source' e publish.ps1 não está disponível." }
    & $publish -Out $Source
}

Stop-Client
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
robocopy $Source $installDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Falha ao copiar os arquivos (robocopy retornou $LASTEXITCODE)." }
Copy-Item $PSCommandPath (Join-Path $installDir 'install.ps1') -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($startMenuLink)
$link.TargetPath = $exe
$link.WorkingDirectory = $installDir
$link.Description = $appName
$link.Save()

New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value $appName
Set-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $version
Set-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value 'AlissonTexeiraDev'
Set-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $installDir
Set-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value $exe
Set-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$installDir\install.ps1`" -Uninstall"
Set-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord

if ($Startup) {
    Set-ItemProperty -Path $runKey -Name $appId -Value "`"$exe`" --minimized"
}

Write-Host ""
Write-Host "$appName instalado em $installDir" -ForegroundColor Green
Write-Host "  Atalho no Menu Iniciar: $appName"
Write-Host "  Iniciar com o Windows : $(if ($Startup) { 'sim' } else { 'não (use -Startup ou a opção no cliente)' })"
Write-Host "  Servidor MCP para o Claude: $mcpExe"

if (-not $NoLaunch) { Start-Process -FilePath $exe }
