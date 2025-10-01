@echo off
echo 清理更新相關的臨時檔案和備份...

REM 刪除備份目錄
if exist "Backup" (
    echo 刪除備份目錄...
    rmdir /s /q "Backup"
)

REM 刪除臨時下載目錄
set TEMP_DIR=%TEMP%\SquealSaga_Update
if exist "%TEMP_DIR%" (
    echo 刪除臨時下載目錄...
    rmdir /s /q "%TEMP_DIR%"
)

echo 清理完成！
echo 現在可以安全地測試更新器了。
pause