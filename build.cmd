@echo off
REM ---------------------------------------------------------------------------
REM  AppGeek build script
REM
REM    build.cmd            -> self-contained single exe (no prerequisites,
REM                            larger download) in .\publish\standalone
REM    build.cmd light      -> framework-dependent exe (~2 MB, needs the
REM                            .NET 8 Desktop Runtime) in .\publish\light
REM    build.cmd installer  -> self-contained build, then compiles
REM                            .\dist\AppGeekSetup.exe with Inno Setup 6
REM                            (free, from https://jrsoftware.org/isdl.php)
REM ---------------------------------------------------------------------------
setlocal
pushd "%~dp0"

if /I "%~1"=="light" goto :light
if /I "%~1"=="installer" goto :installer

:standalone
echo Building self-contained build...
dotnet publish src\AppGeek\AppGeek.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish\standalone
if errorlevel 1 goto :done
echo.
echo Output: %~dp0publish\standalone\AppGeek.exe
goto :done

:light
echo Building framework-dependent build...
dotnet publish src\AppGeek\AppGeek.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true ^
  -o publish\light
if errorlevel 1 goto :done
echo.
echo Output: %~dp0publish\light\AppGeek.exe
echo Requires: .NET 8 Desktop Runtime on the target PC.
goto :done

:installer
echo Building self-contained build...
dotnet publish src\AppGeek\AppGeek.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish\standalone
if errorlevel 1 goto :done

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo.
  echo Inno Setup 6 was not found at:
  echo   %ISCC%
  echo Install it from https://jrsoftware.org/isdl.php and run this again.
  goto :done
)
"%ISCC%" installer\AppGeek.iss
if errorlevel 1 goto :done
echo.
echo Output: %~dp0dist\AppGeekSetup.exe

:done
popd
endlocal
