@echo off
rem PZL-EV – budowa testowego PZL-EV.exe (self-contained, single-file, win-x64).
rem Wymaga .NET 10 SDK na maszynie budujacej. Na maszynach uzytkownikow .NET NIE jest potrzebny.
setlocal
cd /d "%~dp0"

dotnet --version || (echo Brak .NET SDK - zainstaluj .NET 10 SDK & exit /b 1)

rem eFOSS (nexus.global.lmco.com) jest osiagalny tylko przez proxy LM. NuGet nie czyta proxy z NuGet.config
rem w repo - tylko z konfiguracji uzytkownika albo ze zmiennej http_proxy (ustawiana tu, jesli jej brak).
if not defined HTTP_PROXY set HTTP_PROXY=http://proxy-lmi.global.lmco.com:80
echo Proxy NuGet: %HTTP_PROXY%

dotnet restore src\PzlEv.Test.csproj -r win-x64 || (
  echo.
  echo Restore nieudany. NU1301 "nazwa jest prawidlowa..." = brak polaczenia z eFOSS ^(VPN / proxy^),
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
