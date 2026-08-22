@echo off
REM ---------------------------------------------------------------------------
REM  AppGeek build script
REM
REM    build.cmd            -> self-contained single exe (no prerequisites,
REM                            larger download) in .\publish\standalone
REM    build.cmd light      -> framework-dependent exe (~2 MB, needs the
REM                            .NET 8 Desktop Runtime) in .\publish\light
REM ---------------------------------------------------------------------------
setlocal
pushd "%~dp0"

if /I "%~1"=="light" goto :light

echo Building self-contained build...
dotnet publish src\AppGeek\AppGeek.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish\standalone
echo.
echo Output: %~dp0publish\standalone\AppGeek.exe
goto :done

:light
echo Building framework-dependent build...
dotnet publish src\AppGeek\AppGeek.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true ^
  -o publish\light
echo.
echo Output: %~dp0publish\light\AppGeek.exe
echo Requires: .NET 8 Desktop Runtime on the target PC.

:done
popd
endlocal
