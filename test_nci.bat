@echo off
chcp 65001 >nul
rem Test du moteur sur Night City Interactions, avec la vraie DLL Oodle du jeu.
rem 1) lit le mod original (decompression Oodle)  2) fabrique la trad FR (compression Oodle)
cd /d "%~dp0"
set "CMT=%~dp0dist\cmt.exe"
set "MOD=%USERPROFILE%\Downloads\Night City Interactions - Core.zip"
set "TSV=%~dp0test\nci_test.tsv"
set "OUT=%USERPROFILE%\Downloads\Test moteur v0.2"
set "JEU="

if not exist "%CMT%" (
  echo Lance d'abord build.bat pour compiler le programme.
  pause
  exit /b 1
)
if not exist "%MOD%" (
  echo Introuvable : "%MOD%"
  pause
  exit /b 1
)

echo.
echo ===== 1. Lecture du mod original =====
"%CMT%" analyser "%MOD%"
if not errorlevel 1 goto build

echo.
echo La detection automatique du jeu a rate, nouvel essai avec E:\SteamLibrary...
set JEU=--jeu "E:\SteamLibrary\steamapps\common\Cyberpunk 2077"
"%CMT%" analyser "%MOD%" %JEU%
if errorlevel 1 goto fail

:build
echo.
echo ===== 2. Fabrication de la trad FR =====
"%CMT%" construire "%MOD%" "%TSV%" "%OUT%" %JEU%
if errorlevel 1 goto fail

echo.
echo ===== TEST REUSSI =====
echo A installer dans MO2 : "%OUT%\Night City Interactions - Core - FR.zip"
pause
exit /b 0

:fail
echo.
echo ===== ECHEC : fais une capture de cette fenetre et envoie-la a Claude =====
pause
exit /b 1
