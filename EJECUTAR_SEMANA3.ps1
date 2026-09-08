$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repo

Write-Host "================================================"
Write-Host " Trabajo MCOC - Semana 3"
Write-Host " Carga viva, sismo, superposicion y capacidad HA"
Write-Host "================================================"
Write-Host ""

if (-not (Test-Path -LiteralPath ".venv\Scripts\python.exe")) {
    Write-Host "Creando entorno virtual .venv..."
    python -m venv .venv
}

Write-Host "Instalando/actualizando dependencias necesarias..."
& ".venv\Scripts\python.exe" -m pip install openseespy matplotlib

Write-Host ""
Write-Host "Abriendo menu interactivo Semana 3..."
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py"

Write-Host ""
Write-Host "Programa terminado. Esta ventana queda abierta."
Write-Host "Si quieres correrlo otra vez, escribe:"
Write-Host ".\.venv\Scripts\python.exe 'Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py'"
