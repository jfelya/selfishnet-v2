@echo off
REM SelfishNet v2 - portable builds for 64-bit and 32-bit Windows.
REM Each exe is fully self-contained: no .NET install, no redistributables.
setlocal
set OUT=%~dp0..\publish

echo === Publishing win-x64 (64-bit Windows) ===
dotnet publish SelfishNetV2.App\SelfishNetV2.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  -o "%OUT%\x64"
if errorlevel 1 goto :fail

echo === Publishing win-x86 (32-bit Windows) ===
dotnet publish SelfishNetV2.App\SelfishNetV2.App.csproj -c Release -r win-x86 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  -o "%OUT%\x86"
if errorlevel 1 goto :fail

echo.
echo Done:
echo   %OUT%\x64\SelfishNetV2.exe   (Windows 10/11 64-bit)
echo   %OUT%\x86\SelfishNetV2.exe   (Windows 10/11 32-bit)
exit /b 0

:fail
echo PUBLISH FAILED
exit /b 1
