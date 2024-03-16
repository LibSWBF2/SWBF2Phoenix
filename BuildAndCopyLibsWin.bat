@echo off
cls

rem Check if CMake is installed and available
cmake --version >nul 2>&1 || (
    echo Could not find cmake! Are you sure you have it installed and in PATH?
    pause
    exit
)

rem vswhere.exe is guaranteed by Microsoft to be installed in this location:
set vswhere="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist %vswhere% (
    echo Could not find vswhere.exe at: %vswhere% !
    echo Is Visual Studio installed?
    pause
    exit
)

rem By default, MSBuild.exe is not in PATH, so we need to find it ourselfs using vswhere.exe
for /F "tokens=*" %%a in ('%vswhere% -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe') do (set MsBuild=%%a)


rem Check if submodule is initialized
if not exist LibSWBF2\LibSWBF2 (
    echo Could not find LibSWBF2 submodule!
    echo Did you initialized submodules?
    pause
    exit
)

:ChooseOption
echo Choose your build
echo 1. Release (default)
echo 2. Debug

set choice=
set /p choice=Option: 
if not '%choice%'=='' set choice=%choice:~0,1%
if '%choice%'=='' (
    set mode=Release
    goto ChooseThreads
)
if '%choice%'=='1' (
    set mode=Release
    goto ChooseThreads
)
if '%choice%'=='2' (
    set mode=Debug
    goto ChooseThreads
)
echo %choice% is not valid, try again
echo.
goto ChooseOption

:ChooseThreads
echo How many threads should be used for compilation? (default: 4)
set /p choice=Number of Threads:
if '%choice%'=='' (
    set numThreads=4
) else (
    set "var="&for /f "delims=0123456789" %%i in ("%choice%") do set var=%%i
    if defined var (
        echo %choice% is NOT a number!
        goto ChooseThreads
    )
    set numThreads=%choice%
)

:Start

cmake -S "./LibSWBF2/LibSWBF2" -B "./LibSWBF2/LibSWBF2/build" -G "Visual Studio 17 2022" -A x64
cmake --build "./LibSWBF2/LibSWBF2/build" --target ALL_BUILD --parallel --clean-first --config %mode% -- -verbosity:minimal -maxcpucount:%numThreads%

rem restore .NET nuget packages (.NET standard 2.0)
pushd .\LibSWBF2\LibSWBF2.NET
dotnet restore
popd

"%MsBuild%" .\LibSWBF2\LibSWBF2.NET\LibSWBF2.NET.csproj -verbosity:minimal /t:Rebuild /p:Platform="x64" /p:DefineConstants=WIN32 /p:configuration=%mode%
"%MsBuild%" .\lua5.0-swbf2-x64\mak.vs2022\lua50_dll.vcxproj -verbosity:minimal /t:Rebuild /p:Platform="x64" /p:configuration=%mode%

rem Copy built binaries to Unity project
copy ".\LibSWBF2\LibSWBF2\build\%mode%\LibSWBF2.dll" ".\UnityProject\Assets\Lib\LibSWBF2.dll" /Y
copy ".\LibSWBF2\LibSWBF2.NET\bin\x64\%mode%\netstandard2.0\LibSWBF2.NET.dll" ".\UnityProject\Assets\Lib\LibSWBF2.NET.dll" /Y
copy ".\lua5.0-swbf2-x64\bin\x64\%mode%\lua50-swbf2-x64.dll" ".\UnityProject\Assets\Lib\lua50-swbf2-x64.dll" /Y

rem Copy debug database when in Debug mode, delete it otherwise
if '%mode%'=='Debug' (
    copy ".\LibSWBF2\LibSWBF2.NET\bin\x64\%mode%\netstandard2.0\LibSWBF2.NET.pdb" ".\UnityProject\Assets\Lib\LibSWBF2.NET.pdb" /Y
) else (
    rem Delete file without error message (in case it doesn't exist)
    del ".\UnityProject\Assets\Lib\LibSWBF2.NET.pdb" 2>nul
)

echo Done
pause