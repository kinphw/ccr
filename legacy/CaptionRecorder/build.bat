@echo off
rem Build with the .NET Framework 4.x compiler built into Windows (no install needed)
chcp 65001 >nul
set "FW=%windir%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%windir%\Microsoft.NET\Framework\v4.0.30319"
"%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /out:"%~dp0ChromeCaptionRecorder.exe" /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0CaptionRecorder.cs"