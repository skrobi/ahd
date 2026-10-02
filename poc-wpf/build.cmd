@echo off
rem PZL-EV – budowa testowego PZL-EV.exe (self-contained, single-file, win-x64).
rem Wymaga .NET 10 SDK na maszynie budujacej. Na maszynach uzytkownikow .NET NIE jest potrzebny.
setlocal
cd /d "%~dp0"

dotnet --version || (echo Brak .NET SDK - zainstaluj .NET 10 SDK & exit /b 1)

rem eFOSS: nexus.global.lmco.com BEZ proxy (dokumentacja eFOSS, Quick start pkt 4). Gdyby na stanowisku
rem bylo ustawione HTTP_PROXY, wylaczamy je dla domen .lmco.com.
set NO_PROXY=.lmco.com,nexus.global.lmco.com

rem Token eFOSS: lokalny plik eFOSS.local.cmd obok build.cmd (NIE w repozytorium - jest w .gitignore).
rem Tresc pliku (jedna linia):
rem   set NuGetPackageSourceCredentials_eFOSS=Username=^<NTID^>;Password=^<TOKEN^>
if exist "%~dp0eFOSS.local.cmd" call "%~dp0eFOSS.local.cmd"
if not defined NuGetPackageSourceCredentials_eFOSS echo UWAGA: brak tokenu eFOSS - utworz eFOSS.local.cmd ^(README.md^).

dotnet restore src\PzlEv.Test.csproj -r win-x64 || (
  echo.
  echo Restore nieudany. NU1301 = brak polaczenia z eFOSS ^(siec LM / VPN, proxy w konfiguracji NuGet^),
  echo 401 Unauthorized = brak lub niewazny token eFOSS. Patrz README.md, "Pakiety NuGet w srodowisku LM".
  exit /b 1
)
dotnet publish src\PzlEv.Test.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o publish || exit /b 1

echo.
echo Gotowe: %~dp0publish\PZL-EV.exe
echo Skopiuj PZL-EV.exe na dysk sieciowy i uruchom z \\serwer\udzial\... (patrz README.md).
endlocal
