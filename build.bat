@echo off
rem Compile l'appli (CyberpunkModTranslator.exe) et la ligne de commande (cmt.exe) dans le dossier dist
cd /d "%~dp0"
dotnet publish src\App\CyberpunkModTranslator.App.csproj -c Release -o dist
if errorlevel 1 goto fail
dotnet publish src\Cli\CyberpunkModTranslator.Cli.csproj -c Release -o dist
if errorlevel 1 goto fail
echo.
echo Termine : dist\CyberpunkModTranslator.exe
pause
exit /b 0
:fail
echo.
echo Echec de la compilation : fais une capture de cette fenetre.
pause
exit /b 1
