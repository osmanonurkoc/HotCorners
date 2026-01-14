@echo off
:: Set encoding to UTF-8
chcp 65001 >nul

echo Cleaning up artifacts from previous build...
:: Perform cleanup first (Prevents cache issues)
if exist bin rd /s /q bin
if exist obj rd /s /q obj

echo Compiling process starting...
:: 'AssemblyTitle' is a critical parameter for File Description
dotnet publish -c Release -r win-x64 ^
    -p:Version=1.0.0.0 ^
    -p:FileVersion=1.0.0.0 ^
    -p:AssemblyVersion=1.0.0.0 ^
    -p:Company="Osman Onur Koç" ^
    -p:Product="Windows Hot Corners" ^
    -p:AssemblyTitle="Linux Hot Corners implementation for Windows" ^
    -p:Description="Linux Hot Corners implementation for Windows" ^
    -p:Copyright="www.osmanonurkoc.com" ^
    --self-contained true ^
    -p:PublishSingleFile=true

echo.
echo Program compiled successfully!
pause
