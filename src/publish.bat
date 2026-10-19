@echo off
REM SelfishNet v2 - single-file portable build.
REM Produces one standalone SelfishNetV2.exe in ..\publish (no .NET install needed).
dotnet publish SelfishNetV2.App\SelfishNetV2.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -o ..\publish
if %ERRORLEVEL%==0 echo.
if %ERRORLEVEL%==0 echo Done: ..\publish\SelfishNetV2.exe
