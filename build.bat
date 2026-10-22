@echo off
rem CCR build - uses the .NET Framework 4.x compiler built into Windows (no install needed)
chcp 65001 >nul
cd /d "%~dp0"
set "FW=%windir%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%windir%\Microsoft.NET\Framework\v4.0.30319"
set "REFS=/r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%\System.Web.Extensions.dll""
if not exist dist mkdir dist
if not exist ccr.ico (
  "%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /out:dist\ccr.exe %REFS% ccr.cs || exit /b 1
  dist\ccr.exe --make-icon ccr.ico
)
"%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /win32icon:ccr.ico /win32manifest:app.manifest /out:dist\ccr.exe %REFS% ccr.cs || exit /b 1
echo built dist\ccr.exe
rem Package: dist\CCR_v{version}.zip (version taken from the built exe, i.e. AppInfo.Version in ccr.cs)
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item 'dist\ccr.exe').VersionInfo.ProductVersion"`) do set "VER=%%v"
powershell -NoProfile -Command "Compress-Archive -Path 'dist\ccr.exe','README.md' -DestinationPath 'dist\CCR_v%VER%.zip' -Force" || exit /b 1
echo packaged dist\CCR_v%VER%.zip
