@echo off
rem Builds AudioHop.exe with the C# compiler that ships with Windows.
rem No SDK, no NuGet, no MSBuild.

setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find csc.exe under %WINDIR%\Microsoft.NET. Is .NET Framework 4 installed?
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:x64 /codepage:65001 ^
    /out:AudioHop.exe ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /win32icon:assets\AudioHop.ico ^
    /resource:assets\tray.ico,tray.ico ^
    AudioHop.cs Languages.cs
if errorlevel 1 exit /b 1

echo Built AudioHop.exe
endlocal
