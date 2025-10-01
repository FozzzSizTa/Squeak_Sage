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
        private static readonly string TEMP_DOWNLOAD_PATH = Path.Combine(Path.GetTempPath(), "SquealSaga_Update");
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
                    Console.ReadKey();
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
                    
                    var key = Console.ReadKey(true);
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
                Console.WriteLine($"更新過程中發生錯誤: {ex.Message}");
            }

            Console.WriteLine("按任意鍵退出...");
            Console.ReadKey();
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
                // 建立臨時目錄
                if (Directory.Exists(TEMP_DOWNLOAD_PATH))
                    Directory.Delete(TEMP_DOWNLOAD_PATH, true);
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
                Console.WriteLine($"\n❌ 更新失敗: {ex.Message}");
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
            try
            {
                if (Directory.Exists(TEMP_DOWNLOAD_PATH))
                    Directory.Delete(TEMP_DOWNLOAD_PATH, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n警告: 清理臨時檔案失敗: {ex.Message}");
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
                Console.CursorLeft = 0;
                
                // 繪製進度條
                int barWidth = 40;
                int filledWidth = (percentage * barWidth) / 100;
                
                Console.Write("[");
                Console.Write(new string('█', filledWidth));
                Console.Write(new string('░', barWidth - filledWidth));
                Console.Write($"] {percentage:D3}% {message}");
                
                // 清除行尾多餘的字符
                Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - Console.CursorLeft - 1)));
            }
        }
    }
}