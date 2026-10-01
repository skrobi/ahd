@echo off
rem PZL-EV – budowa testowego PZL-EV.exe (self-contained, single-file, win-x64).
rem Wymaga .NET 10 SDK na maszynie budujacej. Na maszynach uzytkownikow .NET NIE jest potrzebny.
setlocal
cd /d "%~dp0"

dotnet --version || (echo Brak .NET SDK - zainstaluj .NET 10 SDK & exit /b 1)

dotnet restore src\PzlEv.Test.csproj -r win-x64 || exit /b 1
dotnet publish src\PzlEv.Test.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o publish || exit /b 1

echo.
echo Gotowe: %~dp0publish\PZL-EV.exe
echo Skopiuj PZL-EV.exe na dysk sieciowy i uruchom z \\serwer\udzial\... (patrz README.md).
endlocal
