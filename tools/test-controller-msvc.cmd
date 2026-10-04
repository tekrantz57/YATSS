@echo off
call "%~1\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /MT "%~2\tests\controller\main.cpp" /Fo"%~2\artifacts\controller-tests\controller-tests.obj" /Fe"%~2\artifacts\controller-tests\controller-tests.exe"
exit /b %errorlevel%
