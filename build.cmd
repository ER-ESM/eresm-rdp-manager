@echo off
rem Baut ERESM-RDP-Manager.exe mit dem .NET-Framework-Compiler (in jedem Windows enthalten).
rem Schritt 1 baut ohne Symbol und exportiert daraus app.ico, Schritt 2 bettet das Symbol ein.
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Security.dll /r:Microsoft.CSharp.dll
%CSC% /out:ERESM-RDP-Manager.exe *.cs || exit /b 1
start "" /wait "%~dp0ERESM-RDP-Manager.exe" --export-icon "%~dp0app.ico"
if not exist app.ico exit /b 1
%CSC% /win32icon:app.ico /out:ERESM-RDP-Manager.exe *.cs || exit /b 1
