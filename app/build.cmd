@echo off
rem PZL-EV – budowa PZL-EV.exe (self-contained, single-file, win-x64): restore, testy, publikacja.
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

dotnet restore src\PzlEv.csproj -r win-x64 || (
  echo.
  echo Restore nieudany. NU1301 = brak polaczenia z eFOSS ^(siec LM / VPN, proxy w konfiguracji NuGet^),
  echo 401 Unauthorized = brak lub niewazny token eFOSS. Patrz README.md, "Pakiety NuGet w srodowisku LM".
  exit /b 1
)
rem Testy logiki (tests\PzlEv.Tests.csproj). Pakiety xUnit przy pierwszym pobraniu trafiaja w eFOSS do zatwierdzenia -
rem do tego czasu testy sa pomijane z ostrzezeniem; nieudany test przerywa publikacje.
dotnet restore tests\PzlEv.Tests.csproj >nul 2>&1
if errorlevel 1 (
  echo UWAGA: pakiety testow ^(xUnit^) niedostepne w eFOSS - testy pominiete.
) else (
  rem Testy SQL na zdalnej bazie TEST trwaja dlugo - kazdy test zaklada tabele migracjami. Wykonywane tylko
  rem przy "build.cmd sql" albo z jawna zmienna PZLEV_TEST_SQL, np. lokalny SQL Server.
  if /i "%~1"=="sql" (
    echo Testy SQL: baza TEST z pzl-ev.json ^(konto AD^) - moga potrwac kilkadziesiat minut.
  ) else if not defined PZLEV_TEST_SQL (
    set PZLEV_SQL_TESTS=0
    echo Testy SQL pominiete - uruchom "build.cmd sql", aby je wykonac ^(README.md^).
  )
  dotnet test tests\PzlEv.Tests.csproj -c Release --no-restore || (echo Testy nie przeszly - publikacja przerwana. & exit /b 1)
)

dotnet publish src\PzlEv.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o publish || exit /b 1

echo.
echo Gotowe: %~dp0publish\PZL-EV.exe
echo Skopiuj PZL-EV.exe na dysk sieciowy i uruchom z \\serwer\udzial\... (patrz README.md).
endlocal
