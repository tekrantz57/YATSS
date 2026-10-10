@echo off
call "%~1\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
where cl >nul 2>nul
if errorlevel 1 (
  echo C++ compiler unavailable after Visual Studio environment setup. Add or repair the MSVC x64/x86 tools and Windows SDK in Visual Studio Installer. 1>&2
  exit /b 1
)
cl /nologo /std:c++17 /EHsc /W4 /WX /MT "%~2\tests\controller\main.cpp" /Fo"%~2\artifacts\controller-tests\controller-tests.obj" /Fe"%~2\artifacts\controller-tests\controller-tests.exe"
exit /b %errorlevel%
