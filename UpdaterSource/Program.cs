using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace GameUpdater
{
    class Program
    {
        private static readonly string LOCAL_README_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
        private static readonly string GITHUB_README_URL = "https://raw.githubusercontent.com/FozzzSizTa/Squeak_Sage/main/README.md";
        private static readonly string GITHUB_RELEASE_URL = "https://github.com/FozzzSizTa/Squeak_Sage/archive/refs/heads/main.zip";
    private static readonly string REPO_GIT_URL = "https://github.com/FozzzSizTa/Squeak_Sage.git";
        private static readonly string TEMP_DOWNLOAD_PATH = Path.Combine(Path.GetTempPath(), "SquealSaga_Update");
    private static readonly object lfsLock = new object();
        private static readonly string BACKUP_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backup");
        private static readonly HttpClient httpClient = new HttpClient();
        
        // 更新器相關檔案，不應被替換
        private static readonly HashSet<string> PROTECTED_FILES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GameUpdater.exe",
            "GameUpdater.pdb",
            "UpdaterSource",
            ".git",
            ".gitignore",
            ".gitattributes",
            "Backup",
            "CleanUpdateFiles.bat"
        };

        private static readonly object consoleLock = new object();

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Squeak Saga 遊戲更新器 ===");
            Console.WriteLine("檢查更新中...");

            try
            {
                // 讀取本地版本
                string localVersion = GetLocalVersion();
                Console.WriteLine($"本地版本: {localVersion}");

                // 讀取遠端版本
                string remoteVersion = await GetRemoteVersionAsync();
                Console.WriteLine($"遠端版本: {remoteVersion}");

                // 比較版本
                if (string.IsNullOrEmpty(localVersion) || string.IsNullOrEmpty(remoteVersion))
                {
                    Console.WriteLine("無法讀取版本資訊，請檢查網路連線或檔案是否存在。");
                    Console.WriteLine("按任意鍵退出...");
                    SafeReadKey();
                    return;
                }

                if (localVersion.Equals(remoteVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("遊戲已是最新版本！");
                }
                else
                {
                    Console.WriteLine("發現新版本！");
                    Console.WriteLine($"是否要更新到版本 {remoteVersion}？(Y/N)");
                    
                    var key = SafeReadKey(true);
                    if (key.Key == ConsoleKey.Y)
                    {
                        await PerformUpdate(remoteVersion);
                    }
                    else
                    {
                        Console.WriteLine("取消更新。");
                    }
                }
            }
            catch (Exception ex)
            {
                // 輸出完整例外以利除錯
                Console.WriteLine($"更新過程中發生錯誤: {ex}");
            }

            Console.WriteLine("按任意鍵退出...");
            SafeReadKey();
        }

        // 安全讀取按鍵：若輸入被重新導向 (stdin 被 pipe)，改用 Console.In 讀取第一個字元
        private static ConsoleKeyInfo SafeReadKey(bool intercept = false)
        {
            try
            {
                if (Console.IsInputRedirected)
                {
                    int ch = Console.In.Read();
                    if (ch == -1)
                        return new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false);

                    char c = (char)ch;
                    var key = ConsoleKey.Enter;
                    try { key = (ConsoleKey)Enum.Parse(typeof(ConsoleKey), char.ToUpper(c).ToString()); } catch { }
                    return new ConsoleKeyInfo(c, key, false, false, false);
                }
                else
                {
                    return Console.ReadKey(intercept);
                }
            }
            catch
            {
                return new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false);
            }
        }

        private static string GetLocalVersion()
        {
            try
            {
                if (!File.Exists(LOCAL_README_PATH))
                {
                    Console.WriteLine($"本地README.md檔案不存在: {LOCAL_README_PATH}");
                    return string.Empty;
                }

                string content = File.ReadAllText(LOCAL_README_PATH);
                return ExtractVersion(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"讀取本地版本失敗: {ex.Message}");
                return string.Empty;
            }
        }

        private static async Task<string> GetRemoteVersionAsync()
        {
            try
            {
                string content = await httpClient.GetStringAsync(GITHUB_README_URL);
                return ExtractVersion(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"讀取遠端版本失敗: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractVersion(string content)
        {
            // 使用正則表達式提取版本號 (Ver-x.x.x)
            var match = Regex.Match(content, @"version:\(Ver-([0-9]+\.[0-9]+\.[0-9]+)\)", RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                return match.Groups[1].Value; // 返回版本號部分 (例如: 0.3.15)
            }

            // 如果沒有找到，嘗試其他格式
            match = Regex.Match(content, @"version[:\s]*([0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }

        private static async Task PerformUpdate(string newVersion)
        {
            Console.WriteLine("開始更新程序...");
            
            try
            {
                // 建立臨時目錄（如存在，使用 CleanupTempFiles 以更穩健地移除）
                if (Directory.Exists(TEMP_DOWNLOAD_PATH))
                    CleanupTempFiles();
                Directory.CreateDirectory(TEMP_DOWNLOAD_PATH);

                // 步驟1: 下載新版本
                await DownloadNewVersion();
                
                // 步驟2: 建立備份
                await CreateBackup();
                
                // 步驟3: 替換檔案
                await ReplaceFiles();
                
                // 步驟4: 清理臨時檔案
                CleanupTempFiles();
                
                // 步驟5: 更新本地README版本
                await UpdateLocalVersion(newVersion);
                
                WriteProgress(100, "更新完成！");
                Console.WriteLine("\n✅ 遊戲已成功更新到版本 " + newVersion);
                Console.WriteLine("請重新啟動遊戲以使用新版本。");
            }
            catch (Exception ex)
            {
                // 輸出完整例外以利除錯
                Console.WriteLine($"\n❌ 更新失敗: {ex}");
                Console.WriteLine("正在嘗試從備份還原...");
                await RestoreFromBackup();
            }
        }

        private static async Task DownloadNewVersion()
        {
            WriteProgress(0, "正在下載新版本...");
            
            string zipPath = Path.Combine(TEMP_DOWNLOAD_PATH, "update.zip");
            
            using (var response = await httpClient.GetAsync(GITHUB_RELEASE_URL, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;
                
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;
                        
                        if (totalBytes > 0)
                        {
                            var progress = (int)((downloadedBytes * 20) / totalBytes); // 20% for download
                            WriteProgress(progress, $"下載中... {downloadedBytes / 1024 / 1024:F1}MB / {totalBytes / 1024 / 1024:F1}MB");
                        }
                    }
                }
            }
            
            WriteProgress(20, "正在解壓縮檔案...");
            
            // 解壓縮
            string extractPath = Path.Combine(TEMP_DOWNLOAD_PATH, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            WriteProgress(30, "檔案解壓縮完成");
        }

        private static async Task CreateBackup()
        {
            WriteProgress(30, "正在建立備份...");
            
            if (Directory.Exists(BACKUP_PATH))
                Directory.Delete(BACKUP_PATH, true);
            Directory.CreateDirectory(BACKUP_PATH);
            
            string gameDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var filesToBackup = Directory.GetFiles(gameDirectory, "*", SearchOption.AllDirectories)
                .Where(file => !IsProtectedFile(file))
                .ToList();
            
            var totalFiles = filesToBackup.Count;
            var processedFiles = 0;
            
            await Task.Run(() =>
            {
                Parallel.ForEach(filesToBackup, file =>
                {
                    try
                    {
                        string relativePath = Path.GetRelativePath(gameDirectory, file);
                        string backupFile = Path.Combine(BACKUP_PATH, relativePath);
                        
                        Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                        File.Copy(file, backupFile, true);
                        
                        Interlocked.Increment(ref processedFiles);
                        var progress = 30 + (processedFiles * 20 / totalFiles); // 20% for backup
                        WriteProgress(progress, $"備份中... {processedFiles}/{totalFiles} 檔案");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\n警告: 無法備份檔案 {file}: {ex.Message}");
                    }
                });
            });
            
            WriteProgress(50, "備份完成");
        }

        private static async Task ReplaceFiles()
        {
            WriteProgress(50, "正在替換檔案...");
            
            string extractedPath = Path.Combine(TEMP_DOWNLOAD_PATH, "extracted");
            string? sourcePath = Directory.GetDirectories(extractedPath).FirstOrDefault();
            
            if (sourcePath == null)
            {
                throw new DirectoryNotFoundException("找不到解壓縮的原始檔案目錄");
            }
            
            string gameDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var filesToReplace = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)
                .Where(file => !IsProtectedSourceFile(file, sourcePath))
                .ToList();
            
            var totalFiles = filesToReplace.Count;
            var processedFiles = 0;
            
            await Task.Run(() =>
            {
                Parallel.ForEach(filesToReplace, file =>
                {
                    try
                    {
                        string relativePath = Path.GetRelativePath(sourcePath, file);
                        string targetFile = Path.Combine(gameDirectory, relativePath);
                        
                        // 確保目標目錄存在
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                        // 若來源是 Git LFS pointer，嘗試以 git-lfs 取得完整檔案並覆寫目標
                        bool handledByLfs = false;
                        try
                        {
                            if (IsGitLfsPointer(file))
                            {
                                lock (lfsLock)
                                {
                                    Console.WriteLine($"\n偵測到 LFS pointer：{relativePath}，嘗試以 git-lfs 取得完整檔...");
                                    handledByLfs = EnsureLfsFileFromGit(REPO_GIT_URL, relativePath, targetFile);
                                    if (!handledByLfs)
                                        Console.WriteLine($"無法以 git-lfs 取得：{relativePath}，將以原始來源覆寫 pointer。");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\nLFS 取得發生例外: {ex.Message}");
                        }

                        // 如果未由 LFS 成功處理，則用來源檔案覆寫
                        if (!handledByLfs)
                        {
                            // 如果目標檔案存在且正在使用中，嘗試幾次
                            for (int retry = 0; retry < 3; retry++)
                            {
                                try
                                {
                                    File.Copy(file, targetFile, true);
                                    break;
                                }
                                catch (IOException) when (retry < 2)
                                {
                                    Thread.Sleep(500); // 等待500ms後重試
                                }
                            }
                        }
                        
                        Interlocked.Increment(ref processedFiles);
                        var progress = 50 + (processedFiles * 40 / totalFiles); // 40% for file replacement
                        WriteProgress(progress, $"替換中... {processedFiles}/{totalFiles} 檔案");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\n警告: 無法替換檔案 {file}: {ex.Message}");
                    }
                });
            });
            
            WriteProgress(90, "檔案替換完成");
        }

        private static async Task UpdateLocalVersion(string newVersion)
        {
            WriteProgress(95, "正在更新版本資訊...");
            
            try
            {
                string readmeContent = await File.ReadAllTextAsync(LOCAL_README_PATH);
                string updatedContent = Regex.Replace(
                    readmeContent,
                    @"version:\(Ver-[0-9]+\.[0-9]+\.[0-9]+\)",
                    $"version:(Ver-{newVersion})",
                    RegexOptions.IgnoreCase
                );
                
                await File.WriteAllTextAsync(LOCAL_README_PATH, updatedContent);
                WriteProgress(98, "版本資訊已更新");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n警告: 無法更新版本資訊: {ex.Message}");
            }
        }

        private static async Task RestoreFromBackup()
        {
            try
            {
                if (!Directory.Exists(BACKUP_PATH))
                {
                    Console.WriteLine("找不到備份檔案，無法還原。");
                    return;
                }
                
                Console.WriteLine("正在從備份還原檔案...");
                string gameDirectory = AppDomain.CurrentDomain.BaseDirectory;
                
                var backupFiles = Directory.GetFiles(BACKUP_PATH, "*", SearchOption.AllDirectories);
                
                await Task.Run(() =>
                {
                    foreach (var backupFile in backupFiles)
                    {
                        string relativePath = Path.GetRelativePath(BACKUP_PATH, backupFile);
                        string targetFile = Path.Combine(gameDirectory, relativePath);
                        
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                        File.Copy(backupFile, targetFile, true);
                    }
                });
                
                Console.WriteLine("✅ 已從備份成功還原檔案。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 還原失敗: {ex.Message}");
            }
        }

        private static void CleanupTempFiles()
        {
            if (!Directory.Exists(TEMP_DOWNLOAD_PATH))
                return;

            const int maxAttempts = 6;
            int attempt = 0;
            Exception? lastEx = null;

            while (attempt < maxAttempts && Directory.Exists(TEMP_DOWNLOAD_PATH))
            {
                attempt++;
                try
                {
                    // 移除所有檔案的 ReadOnly 屬性
                    try
                    {
                        var files = Directory.GetFiles(TEMP_DOWNLOAD_PATH, "*", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                        }
                    }
                    catch { }

                    // 嘗試刪除整個目錄
                    Directory.Delete(TEMP_DOWNLOAD_PATH, true);

                    // 成功刪除則跳出
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Console.WriteLine($"警告: 第 {attempt} 次嘗試刪除暫存資料夾失敗: {ex.Message}");

                    // 列出無法刪除的檔案並嘗試開啟以檢查是否被鎖定
                    try
                    {
                        var files = Directory.GetFiles(TEMP_DOWNLOAD_PATH, "*", SearchOption.AllDirectories);
                        foreach (var f in files.Take(50)) // 列出最多 50 個做診斷
                        {
                            try
                            {
                                using (var fs = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                {
                                    // able to open exclusively
                                }
                            }
                            catch (Exception openEx)
                            {
                                Console.WriteLine($"無法以獨占方式開啟: {f} -> {openEx.Message}");
                            }
                        }
                    }
                    catch (Exception listEx)
                    {
                        Console.WriteLine($"列出暫存檔案時發生錯誤: {listEx.Message}");
                    }

                    // 等待後重試（逐步增加等待時間）
                    Thread.Sleep(250 * attempt);
                }
            }

            if (Directory.Exists(TEMP_DOWNLOAD_PATH))
            {
                Console.WriteLine($"警告: 無法刪除暫存資料夾 {TEMP_DOWNLOAD_PATH}，最後例外: {lastEx?.Message}");
            }
        }

        // 檢查檔案是否為 Git LFS pointer
        private static bool IsGitLfsPointer(string filePath)
        {
            try
            {
                using (var sr = new StreamReader(filePath))
                {
                    var firstLine = sr.ReadLine();
                    return firstLine != null && firstLine.StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        // 執行外部命令，回傳是否成功以及 stdout/stderr
        private static bool RunProcess(string fileName, string arguments, string workingDirectory, out string stdOut, out string stdErr, int timeoutMs = 300000)
        {
            stdOut = string.Empty;
            stdErr = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi)!)
                {
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        stdErr = "Process timeout";
                        return false;
                    }

                    stdOut = outTask.Result ?? string.Empty;
                    stdErr = errTask.Result ?? string.Empty;
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                stdErr = ex.Message;
                return false;
            }
        }

        // 使用 git + git-lfs clone 並拉取特定檔案，然後複製到 destinationPath
        // 回傳是否成功
        private static bool EnsureLfsFileFromGit(string repoUrl, string relativePath, string destinationPath)
        {
            string clonePath = Path.Combine(TEMP_DOWNLOAD_PATH, "gitclone");

            // 檢查 git 是否存在
            if (!RunProcess("git", "--version", AppDomain.CurrentDomain.BaseDirectory, out var gvOut, out var gvErr))
            {
                Console.WriteLine($"無法找到 git: {gvErr}");
                return false;
            }

            try
            {
                if (Directory.Exists(clonePath))
                    Directory.Delete(clonePath, true);
                Directory.CreateDirectory(clonePath);

                Console.WriteLine($"正在以 git clone 取得 LFS 檔案（暫存於 {clonePath}）...");

                // git clone --depth 1 <repo> <clonePath>
                if (!RunProcess("git", $"clone --depth 1 {repoUrl} \"{clonePath}\"", TEMP_DOWNLOAD_PATH, out var cloneOut, out var cloneErr))
                {
                    Console.WriteLine($"git clone 失敗: {cloneErr}\n{cloneOut}");
                    return false;
                }

                // 啟用 git lfs
                RunProcess("git", "lfs install", clonePath, out var lfsInstallOut, out var lfsInstallErr);

                // 把路徑轉成 Unix style for git lfs include
                var includePath = relativePath.Replace("\\", "/");

                // 嘗試直接 pull 指定檔案
                if (!RunProcess("git", $"lfs pull --include=\"{includePath}\"", clonePath, out var pullOut, out var pullErr))
                {
                    // 如果 pull 失敗，嘗試 fetch + checkout
                    Console.WriteLine($"git lfs pull 失敗，嘗試 fetch + checkout: {pullErr}");
                    RunProcess("git", $"lfs fetch --include=\"{includePath}\" --all", clonePath, out var fetchOut, out var fetchErr);
                    RunProcess("git", "lfs checkout", clonePath, out var coOut, out var coErr);
                }

                string sourceFile = Path.Combine(clonePath, relativePath);
                if (!File.Exists(sourceFile))
                {
                    Console.WriteLine($"在 clone 的 repo 中找不到檔案: {sourceFile}");
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourceFile, destinationPath, true);
                Console.WriteLine($"已從 git-lfs 取得並覆寫: {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"嘗試從 git-lfs 取得檔案失敗: {ex.Message}");
                return false;
            }
            finally
            {
                // 清理 clone 資料夾（保留以利除錯，這裡選擇刪除）
                try { if (Directory.Exists(clonePath)) Directory.Delete(clonePath, true); } catch { }
            }
        }

        private static bool IsProtectedFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string relativePath = Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, filePath);
            
            // 基本保護檔案檢查
            if (PROTECTED_FILES.Any(protectedFile => 
                fileName.Equals(protectedFile, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(protectedFile, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            
            // 額外保護Git相關檔案和隱藏檔案
            if (relativePath.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(".", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            return false;
        }

        private static bool IsProtectedSourceFile(string filePath, string sourcePath)
        {
            string relativePath = Path.GetRelativePath(sourcePath, filePath);
            string fileName = Path.GetFileName(filePath);
            
            // 保護更新器相關檔案
            if (PROTECTED_FILES.Any(protectedFile => 
                relativePath.StartsWith(protectedFile, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(protectedFile, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            
            // 額外保護Git相關檔案和隱藏檔案
            if (relativePath.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(".", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            return false;
        }

        private static void WriteProgress(int percentage, string message)
        {
            lock (consoleLock)
            {
                try
                {
                    // 如果輸出被重導向（寫到檔案或管線），避免使用游標/視窗寬度操作
                    if (Console.IsOutputRedirected)
                    {
                        Console.WriteLine($"[{percentage:D3}%] {message}");
                        return;
                    }

                    Console.CursorLeft = 0;

                    // 繪製進度條
                    int barWidth = 40;
                    int filledWidth = (percentage * barWidth) / 100;

                    Console.Write("[");
                    Console.Write(new string('█', filledWidth));
                    Console.Write(new string('░', barWidth - filledWidth));
                    Console.Write($"] {percentage:D3}% {message}");

                    // 清除行尾多餘的字符（保護性 try）
                    try
                    {
                        int remaining = Math.Max(0, Console.WindowWidth - Console.CursorLeft - 1);
                        Console.Write(new string(' ', remaining));
                    }
                    catch
                    {
                        // 忽略在不支援 WindowWidth/游標操作時的錯誤
                    }
                }
                catch (IOException)
                {
                    // 在某些環境（如重導向、沒有控制台）上，Console 屬性存取會失敗，改用簡單輸出
                    Console.WriteLine($"[{percentage:D3}%] {message}");
                }
            }
        }
    }
}