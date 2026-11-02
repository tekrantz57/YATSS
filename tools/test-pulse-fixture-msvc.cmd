@echo off
call "%~1\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /MT "%~2\tests\pulse-fixture\main.cpp" /Fo"%~2\artifacts\pulse-fixture\pulse.obj" /Fe"%~2\artifacts\pulse-fixture\pulse.exe"
exit /b %errorlevel%
