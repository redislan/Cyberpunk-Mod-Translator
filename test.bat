@echo off
chcp 65001 >nul
rem Tests du moteur : faux mods fabriques dans un dossier temporaire, passes par le moteur, resultat verifie.
rem A lancer avant chaque nouvelle version. Pas besoin du jeu ni de sa DLL.
cd /d "%~dp0"
dotnet run --project test\CyberpunkModTranslator.Tests -c Release
if errorlevel 1 goto fail
echo.
echo ===== TOUS LES TESTS PASSENT =====
pause
exit /b 0
:fail
echo.
echo ===== ECHEC : fais une capture de cette fenetre et envoie-la a Claude =====
pause
exit /b 1
