@echo off
echo === Unity遊戲快速更新腳本 ===
echo.

REM 檢查Git狀態
echo 1. 檢查Git狀態...
git status --porcelain > temp_status.txt
for /f %%i in ("temp_status.txt") do set size=%%~zi
if %size% gtr 0 (
    echo 發現未提交的變更，正在提交...
    git add .
    git commit -m "保存Unity Build前的狀態"
) else (
    echo Git狀態乾淨，可以繼續
)
del temp_status.txt

echo.
echo 2. Unity Build完成後，請按任意鍵繼續...
pause > nul

REM 檢查README版本號
echo 3. 請確認README.md中的版本號已更新
echo 當前README內容：
type README.md
echo.
echo 版本號是否已更新？ (Y/N)
set /p version_updated="輸入Y繼續，N取消: "
if /i not "%version_updated%"=="Y" (
    echo 請先更新README.md中的版本號
    pause
    exit /b 1
)

REM 提交更新
echo.
echo 4. 提交更新到Git...
git add .
set /p commit_msg="輸入版本號 (如: 0.3.16): "
git commit -m "feat: Update game to version %commit_msg%"

echo.
echo 5. 推送到GitHub...
git push

echo.
echo ✅ 更新完成！
echo 新版本已成功推送到GitHub
echo 使用者現在可以透過GameUpdater.exe獲取更新
pause