@echo off
REM Portable single-file exe: needs no .NET install, no terminal.
REM Double-click it (Windows will ask for Administrator automatically).
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
copy /Y "bin\Release\net8.0-windows\win-x64\publish\SelfishNetV2.exe" "SelfishNetV2.exe"
echo.
echo Portable exe at: SelfishNetV2.exe (project root)
