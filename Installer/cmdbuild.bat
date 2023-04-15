echo off

rem cmd.exe is needed for this

SETLOCAL EnableExtensions DisableDelayedExpansion

rem trick to get absolute path
set exefolder=..\\EDDiscovery\\bin\\Release\\
pushd %exefolder%
set absexefolder=%CD%
popd
set exefile=%absexefolder%\EDDiscovery.exe

rem using EnableExtensions, use the pattern replacer to double \\

set exefile=%exefile:\=\\%
set vno=%1

echo ExeFile is %exefile%, Version `%vno%`


if "%vno%"=="" goto :errorVER

wmic datafile where Name="%exefile%" get Version |more >%TMP%\vno.txt

find "%vno%" %TMP%\vno.txt
if %ERRORLEVEL%==1 goto :errorEXE
echo Exe passed

find "%vno%" ..\eddiscovery\properties\AssemblyInfo.cs
if %ERRORLEVEL%==1 goto :errorAI
echo Assembly passed

echo.
echo Build %vno%
"\Program Files (x86)\Inno Setup 6\iscc.exe" /DMyAppVersion=%vno% innoscript.iss
copy ..\EDDiscovery\bin\Release\EDDiscovery.Portable.Zip installers\EDDiscovery.Portable.%vno%.zip
certutil -hashfile installers\EDDiscovery-%vno%.exe SHA256 >installers\checksums.%vno%.txt
certutil -hashfile installers\EDDiscovery.Portable.%vno%.zip SHA256 >>installers\checksums.%vno%.txt

exit /b

:errorAI
echo Version %vno% does not correspond to AssemblyInfo.cs or to the release exe
exit /b

:errorEXE
echo Version %vno% does not correspond to the release exe
exit /b

:errorVER
echo Must give version on command line, example: build 16.1.1
exit /b


