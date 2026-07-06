@echo off
chcp 65001 >nul
echo ==============================================
echo  正在清理 C# 项目编译垃圾 (bin / obj / 缓存)
echo  此操作安全，不会删除任何代码！
echo ==============================================
echo.

cd /d "%~dp0"

:: 删除所有bin目录
for /d /r %%i in (bin) do (
    echo 正在删除：%%i
    rd /s /q "%%i" >nul 2>nul
)
:: 删除所有obj目录
for /d /r %%i in (obj) do (
    echo 正在删除：%%i
    rd /s /q "%%i" >nul 2>nul
)
:: VS缓存文件
del /s /q *.suo >nul 2>nul
del /s /q *.user >nul 2>nul
rd /s /q .vs >nul 2>nul

echo.
echo ==============================================
echo  ✅ 清理完成！所有编译垃圾已删除
echo ==============================================
echo.
pause