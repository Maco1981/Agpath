@echo off
rem Builds AgPath.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Nothing needs to be installed. Output: AgPath.exe in this folder, log in build_log.txt.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:AgPath.exe /win32icon:icon.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll src\*.cs > build_log.txt 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> build_log.txt
  type build_log.txt
  exit /b 1
)
"%CSC%" /nologo /codepage:65001 /target:exe /out:tools\TestSender.exe tools\TestSender.cs >> build_log.txt 2>&1
echo OK >> build_log.txt
type build_log.txt
