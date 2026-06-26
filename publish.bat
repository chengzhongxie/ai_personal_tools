@echo off
echo ========================================
echo   PersonalAssistant - 单文件发布打包
echo ========================================
echo.

cd /d "%~dp0"

echo [1/2] 清理旧的发布目录...
if exist "publish" rmdir /s /q "publish"

echo [2/2] 开始发布 (Release + 单文件)...
dotnet publish -c Release -o publish --nologo

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [错误] 发布失败!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================
echo   发布完成!
echo ========================================
echo.
echo 产物目录: %cd%\publish\
echo.
echo publish\
echo   智伴.exe
echo   Assets\
echo     qwen2.5-0.5b-instruct-q4_k_m.gguf
echo     model_sources.json
echo.
echo 分发时打包整个 publish\ 文件夹即可。
echo.
pause
