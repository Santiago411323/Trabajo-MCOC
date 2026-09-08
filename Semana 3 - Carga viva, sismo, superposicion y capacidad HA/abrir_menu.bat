@echo off
setlocal
cd /d "%~dp0.."
".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --menu
pause
