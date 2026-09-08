@echo off
setlocal
cd /d "%~dp0"

echo ================================================
echo  Trabajo MCOC - Semana 3
echo  Carga viva, sismo, superposicion y capacidad HA
echo ================================================
echo.

if not exist ".venv\Scripts\python.exe" (
    echo Creando entorno virtual .venv...
    python -m venv .venv
    if errorlevel 1 (
        echo ERROR: No se pudo crear .venv. Revisa que Python este instalado.
        pause
        exit /b 1
    )
)

echo Instalando/actualizando dependencias necesarias...
".venv\Scripts\python.exe" -m pip install openseespy matplotlib
if errorlevel 1 (
    echo ERROR: No se pudieron instalar las dependencias.
    pause
    exit /b 1
)

echo.
echo Abriendo menu interactivo Semana 3...
".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py"
echo.
pause
