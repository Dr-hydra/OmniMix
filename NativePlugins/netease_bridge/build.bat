@echo off
setlocal EnableDelayedExpansion

rem ChillNetease.dll Build Script
rem This script clones go-musicfox, copies netease_bridge, and builds the DLL

rem Add Go to PATH when it is installed in common locations.
where go >nul 2>&1 || (
    if exist "C:\Program Files\Go\bin\go.exe" set "PATH=C:\Program Files\Go\bin;%PATH%"
    if exist "D:\Program Files\Go\bin\go.exe" set "PATH=D:\Program Files\Go\bin;%PATH%"
)

rem Prefer CC from the environment. Otherwise use clang/lld or gcc.
rem clang -fuse-ld=lld avoids MSVC link.exe for Go cgo builds.
if not defined CC (
    where clang >nul 2>&1 && set CC=clang -fuse-ld=lld
)
if not defined CC (
    where gcc >nul 2>&1 && set CC=gcc
)
if defined CC echo Using C compiler: %CC%
set CGO_ENABLED=1
set GOOS=windows
set GOARCH=amd64

rem Paths and upstream settings.
set "SCRIPT_DIR=%~dp0"
rem Project root is two levels above NativePlugins\netease_bridge.
set "PROJECT_ROOT=%SCRIPT_DIR%..\.."
set "BUILD_DIR=%PROJECT_ROOT%\build\netease_bridge"
set "SOURCE_DIR=%BUILD_DIR%\go-musicfox-src"
set "WORK_DIR=%BUILD_DIR%\go-musicfox-work"
set "GOMUSICFOX_URL=https://github.com/go-musicfox/go-musicfox.git"
set "GOMUSICFOX_BRANCH=master"
set "OUTPUT_DIR=%PROJECT_ROOT%\bin\native\x64"

echo ===========================================
echo ChillNetease.dll Build Script
echo ===========================================
echo.

rem Check Go.
echo [1/5] Checking Go version...
go version
if errorlevel 1 (
    echo ERROR: Go not found! Please install Go from https://go.dev/
    exit /b 1
)

rem Check C compiler.
echo.
echo [2/5] Checking C compiler (%CC%)...
%CC% --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: C compiler not found! Set CC=clang or install MinGW-w64
    exit /b 1
)
%CC% --version | findstr /i "gcc clang" >nul 2>&1

rem Check Git.
echo.
echo [3/5] Checking Git...
git --version | findstr "git"
if errorlevel 1 (
    echo ERROR: Git not found! Please install Git
    exit /b 1
)

rem Prepare build directory.
echo.
echo [4/5] Preparing build directory...
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"

rem Keep a clean go-musicfox source cache. Build-time generated files are written
rem only to WORK_DIR so git fetch is not blocked by vendor/go.mod changes.
echo.
if exist "%SOURCE_DIR%\.git" (
    echo Updating go-musicfox source cache...
    cd /d "%SOURCE_DIR%"
    git fetch --depth 1 origin %GOMUSICFOX_BRANCH%
    if errorlevel 1 (
        echo WARNING: git fetch failed, using cached go-musicfox source...
    ) else (
        git checkout -B %GOMUSICFOX_BRANCH% FETCH_HEAD
        if errorlevel 1 (
            echo ERROR: Failed to checkout go-musicfox source cache!
            exit /b 1
        )
    )
) else (
    echo Cloning go-musicfox source cache...
    if exist "%SOURCE_DIR%" rmdir /s /q "%SOURCE_DIR%"
    git clone --depth 1 --branch %GOMUSICFOX_BRANCH% %GOMUSICFOX_URL% "%SOURCE_DIR%"
    if errorlevel 1 (
        echo ERROR: Failed to clone go-musicfox!
        exit /b 1
    )
)

rem Prepare a disposable work directory for go get/vendor/patch changes.
echo.
echo Preparing disposable go-musicfox work directory...
cd /d "%BUILD_DIR%"
if exist "%WORK_DIR%" rmdir /s /q "%WORK_DIR%"
git clone "%SOURCE_DIR%" "%WORK_DIR%" >nul
if errorlevel 1 (
    echo ERROR: Failed to prepare go-musicfox work directory!
    exit /b 1
)

rem Copy local netease_bridge sources.
echo.
echo Copying netease_bridge source code...
if not exist "%WORK_DIR%\netease_bridge" mkdir "%WORK_DIR%\netease_bridge"
copy /Y "%SCRIPT_DIR%*.go" "%WORK_DIR%\netease_bridge\" >nul
copy /Y "%SCRIPT_DIR%.gitignore" "%WORK_DIR%\netease_bridge\" 2>nul

rem Enter the go-musicfox work directory.
cd /d "%WORK_DIR%"

rem Add netease_bridge dependencies that go-musicfox does not vendor.
echo.
echo Adding extra dependencies to vendor...
go get github.com/telanflow/cookiejar@latest >nul 2>&1
go mod vendor >nul 2>&1

rem Replace the upstream placeholder NMTID cookie rejected by NetEase servers.
powershell -Command "$f='vendor\github.com\go-musicfox\netease-music\util\common.go'; $c=[IO.File]::ReadAllText($f,[Text.Encoding]::UTF8); $c=$c.Replace('some_random_id_from_strategy','nmtid_'+[guid]::NewGuid().ToString('N').Substring(0,16)); [IO.File]::WriteAllText($f,$c,[Text.Encoding]::UTF8)" >nul 2>&1

rem Build DLL.
echo.
echo [5/5] Building DLL...
go build -buildmode=c-shared -o netease_bridge\ChillNetease.dll -ldflags "-s -w" ./netease_bridge

if errorlevel 1 (
    echo.
    echo ERROR: Build failed!
    exit /b 1
)

echo.
echo ===========================================
echo Build successful!
echo ===========================================

rem Copy output files.
echo.
echo Copying output files...
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

copy /Y "netease_bridge\ChillNetease.dll" "%OUTPUT_DIR%\"
copy /Y "netease_bridge\ChillNetease.h" "%OUTPUT_DIR%\" 2>nul

rem Also copy to netease_bridge for debugging.
copy /Y "netease_bridge\ChillNetease.dll" "%SCRIPT_DIR%\"
copy /Y "netease_bridge\ChillNetease.h" "%SCRIPT_DIR%\" 2>nul

rem Also copy to the Netease module directory for packaging and local tests.
if exist "%PROJECT_ROOT%\OmniMixPlayer\modules\Netease\native\x64" (
    copy /Y "netease_bridge\ChillNetease.dll" "%PROJECT_ROOT%\OmniMixPlayer\modules\Netease\native\x64\"
)

echo.
echo Output files:
echo   - %OUTPUT_DIR%\ChillNetease.dll
echo   - %SCRIPT_DIR%ChillNetease.dll (debug copy)
echo.

rem Optional full build-cache cleanup:
rem echo Cleaning up build directory...
rem cd /d "%PROJECT_ROOT%"
rem rmdir /s /q "%BUILD_DIR%"

echo Done!
if "%1"=="" pause
