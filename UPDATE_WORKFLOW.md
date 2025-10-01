# Unity遊戲更新流程指南

## 🔄 版本更新步驟

### 1. 更新前準備
```cmd
# 確認當前狀態乾淨
git status

# 如果有未提交的更改，先提交
git add .
git commit -m "保存更新前狀態"
```

### 2. Unity Build
- 在Unity中修改版本號（如：0.3.15 → 0.3.16）
- Build到當前目錄：`d:\Unity\SS\`
- Unity會自動覆蓋遊戲檔案

### 3. 更新README版本
- 手動編輯 `README.md`
- 修改版本號：`version:(Ver-0.3.16)`

### 4. 驗證更新器
```cmd
# 測試更新器是否正常（可選）
.\GameUpdater.exe
```

### 5. 提交到Git
```cmd
# 查看變更
git status

# 添加所有變更
git add .

# 提交新版本
git commit -m "feat: Update game to version 0.3.16"

# 推送到GitHub
git push
```

## ⚠️ 注意事項

### ✅ 會被Unity覆蓋的檔案（正常）：
- `Squeak_Saga.exe` - 遊戲主程式
- `Squeak_Saga_Data/` - 遊戲數據
- `MonoBleedingEdge/` - Unity運行環境
- `UnityPlayer.dll` - Unity播放器

### 🛡️ 受保護的檔案（不會被覆蓋）：
- `GameUpdater.exe` - 更新器
- `UpdaterSource/` - 更新器源碼
- `.git/` - Git版本控制
- `README.md` - 需要手動更新版本號

## 🎯 快速檢查清單
- [ ] Unity Build完成
- [ ] README.md版本號已更新
- [ ] GameUpdater.exe存在且功能正常
- [ ] Git status顯示預期的檔案變更
- [ ] 已推送到GitHub