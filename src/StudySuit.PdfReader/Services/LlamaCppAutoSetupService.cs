using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace StudySuit.PdfReader.Services;

public sealed record SystemHardwareSpecs(
    double TotalRamGb,
    int CpuCores,
    bool Is64Bit,
    string TierName,
    string SummaryText);

public sealed record LlamaModelPackage(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    long ApproximateSizeBytes,
    string RamRecommendation,
    string Description);

public interface ILlamaCppAutoSetupService : IDisposable
{
    SystemHardwareSpecs DetectSystemSpecs();
    IReadOnlyList<LlamaModelPackage> GetAvailablePackages();
    LlamaModelPackage GetRecommendedPackage(SystemHardwareSpecs specs);
    string GetLlamaCppDirectory();
    string GetBinDirectory();
    string GetModelsDirectory();
    string? FindInstalledServerExecutable();
    IReadOnlyList<string> GetDownloadedModelFiles();
    Task<string> SetupLlamaServerAsync(
        IProgress<(string Phase, double Percent, string Detail)>? progress = null,
        CancellationToken ct = default);
    Task<string> DownloadModelAsync(
        LlamaModelPackage package,
        IProgress<(string Phase, double Percent, string Detail)>? progress = null,
        CancellationToken ct = default);
    string? LastServerError { get; }
    Task<bool> StartServerAsync(string modelFileName, int port = 9931, CancellationToken ct = default);
    void StopServer();
    bool IsServerRunning();
}

public sealed partial class LlamaCppAutoSetupService : ILlamaCppAutoSetupService
{
    private const string LlamaZipDownloadUrl =
        "https://github.com/ggml-org/llama.cpp/releases/download/b12321/llama-b12321-bin-win-cpu-x64.zip";

    public static readonly IReadOnlyList<LlamaModelPackage> AvailablePackages =
    [
        new(
            "qwen-1.5b",
            "Qwen 2.5 1.5B Instruct (Ultra-Light • ~1.0 GB)",
            "qwen2.5-1.5b-instruct-q4_k_m.gguf",
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            1117320736,
            "Works smoothly on ANY laptop/PC (2GB–8GB RAM)",
            "Fastest response, zero lag, smooth multitasking while studying."),
        new(
            "qwen-3b",
            "Qwen 2.5 3B Instruct (Balanced • ~1.9 GB)",
            "qwen2.5-3b-instruct-q4_k_m.gguf",
            "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
            2070000000,
            "Recommended for 8GB–16GB RAM PCs",
            "Balanced speed & strong reasoning for exam prep and complex PDFs."),
        new(
            "qwen-7b",
            "Qwen 2.5 7B Instruct (Advanced • ~4.4 GB)",
            "qwen2.5-7b-instruct-q4_k_m.gguf",
            "https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF/resolve/main/qwen2.5-7b-instruct-q4_k_m.gguf",
            4680000000,
            "Requires 16GB+ RAM or Dedicated GPU",
            "Deep UPSC Mains analysis & maximum academic reasoning capability.")
    ];

    private readonly HttpClient _http;
    private readonly string _rootDir;
    private readonly string _binDir;
    private readonly string _modelsDir;
    private Process? _serverProcess;

    public string? LastServerError { get; private set; }

    public LlamaCppAutoSetupService(HttpClient? httpClient = null, string? customRootDir = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromHours(2) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 StudyMateAI/1.0");
        }
        _rootDir = customRootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader",
            "llamacpp");

        _binDir = Path.Combine(_rootDir, "bin");
        _modelsDir = Path.Combine(_rootDir, "models");

        try
        {
            Directory.CreateDirectory(_binDir);
            Directory.CreateDirectory(_modelsDir);
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public SystemHardwareSpecs DetectSystemSpecs()
    {
        double totalRamGb = 8.0;
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                totalRamGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
            }
        }
        catch
        {
        }

        int cpuCores = Math.Max(1, Environment.ProcessorCount);
        bool is64 = Environment.Is64BitOperatingSystem;

        string tierName;
        if (totalRamGb < 8.0 || cpuCores <= 4)
        {
            tierName = "Lightweight Tier (1.5B Recommended)";
        }
        else if (totalRamGb < 16.0)
        {
            tierName = "Balanced Tier (3B Recommended)";
        }
        else
        {
            tierName = "High Performance Tier (3B or 7B)";
        }

        string summary = $"{totalRamGb:F1} GB RAM • {cpuCores} CPU Cores • {(is64 ? "64-bit OS" : "32-bit OS")} ({tierName})";
        return new SystemHardwareSpecs(totalRamGb, cpuCores, is64, tierName, summary);
    }

    public IReadOnlyList<LlamaModelPackage> GetAvailablePackages() => AvailablePackages;

    public LlamaModelPackage GetRecommendedPackage(SystemHardwareSpecs specs)
    {
        if (specs.TotalRamGb < 8.0 || specs.CpuCores <= 4)
        {
            return AvailablePackages[0]; // 1.5B
        }

        if (specs.TotalRamGb >= 16.0 && specs.CpuCores >= 8)
        {
            return AvailablePackages[1]; // 3B is balanced & lightning fast on 16GB
        }

        return AvailablePackages[1]; // 3B
    }

    public string GetLlamaCppDirectory() => _rootDir;
    public string GetBinDirectory() => _binDir;
    public string GetModelsDirectory() => _modelsDir;

    public string? FindInstalledServerExecutable()
    {
        string localExe = Path.Combine(_binDir, "llama-server.exe");
        if (File.Exists(localExe))
        {
            return localExe;
        }

        string legacyExe = Path.Combine(_binDir, "server.exe");
        if (File.Exists(legacyExe))
        {
            return legacyExe;
        }

        if (Directory.Exists(_binDir))
        {
            string? foundInSub = Directory.GetFiles(_binDir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (foundInSub is not null)
            {
                return foundInSub;
            }
        }

        // Check if llama-server is installed in PATH
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "llama-server.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string output = p?.StandardOutput.ReadLine()?.Trim() ?? string.Empty;
            p?.WaitForExit();
            if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
            {
                return output;
            }
        }
        catch
        {
        }

        return null;
    }

    public IReadOnlyList<string> GetDownloadedModelFiles()
    {
        try
        {
            if (!Directory.Exists(_modelsDir))
            {
                return [];
            }

            return Directory.GetFiles(_modelsDir, "*.gguf")
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<string> SetupLlamaServerAsync(
        IProgress<(string Phase, double Percent, string Detail)>? progress = null,
        CancellationToken ct = default)
    {
        StopServer();
        try
        {
            foreach (var p in Process.GetProcessesByName("llama-server"))
            {
                try { p.Kill(true); p.WaitForExit(1000); } catch { }
            }
        }
        catch { }

        string? existing = FindInstalledServerExecutable();
        if (existing is not null)
        {
            progress?.Report(("llama.cpp is already installed", 100, existing));
            return existing;
        }

        progress?.Report(("Downloading llama.cpp runtime...", 5, "Connecting to GitHub..."));
        string zipPath = Path.Combine(_rootDir, "llama-runtime.zip");

        try
        {
            using (var resp = await _http.GetAsync(LlamaZipDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long totalBytes = resp.Content.Headers.ContentLength ?? 25000000;
                long totalRead = 0;

                await using var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                byte[] buffer = new byte[81920];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    totalRead += read;
                    double pct = Math.Min(95.0, (double)totalRead / totalBytes * 90.0);
                    string mbStr = $"{totalRead / (1024.0 * 1024.0):F1} MB / {totalBytes / (1024.0 * 1024.0):F1} MB";
                    progress?.Report(("Downloading llama.cpp runtime...", pct, mbStr));
                }
            }

            progress?.Report(("Extracting runtime binaries...", 95, "Unzipping..."));
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(zipPath, _binDir, overwriteFiles: true);
            }, ct).ConfigureAwait(false);

            // Write helper batch script for manual launching if needed
            string batPath = Path.Combine(_rootDir, "run-server.bat");
            string batContent = "@echo off\r\ncd /d \"%~dp0\"\r\nbin\\llama-server.exe -m \"%~1\" --port 9931 --host 127.0.0.1 -c 2048\r\n";
            await File.WriteAllTextAsync(batPath, batContent, ct).ConfigureAwait(false);

            string targetExe = Path.Combine(_binDir, "llama-server.exe");
            progress?.Report(("llama.cpp runtime installed!", 100, targetExe));
            return targetExe;
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
            }
        }
    }

    public async Task<string> DownloadModelAsync(
        LlamaModelPackage package,
        IProgress<(string Phase, double Percent, string Detail)>? progress = null,
        CancellationToken ct = default)
    {
        string targetFile = Path.Combine(_modelsDir, package.FileName);
        if (File.Exists(targetFile))
        {
            var info = new FileInfo(targetFile);
            if (info.Length >= package.ApproximateSizeBytes * 0.95)
            {
                progress?.Report(("Model already installed", 100, package.FileName));
                return targetFile;
            }
        }

        string tempFile = targetFile + ".download";
        progress?.Report(($"Downloading {package.DisplayName}...", 0, "Connecting to Hugging Face..."));

        using (var req = new HttpRequestMessage(HttpMethod.Get, package.DownloadUrl))
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? package.ApproximateSizeBytes;
            long totalRead = 0;

            await using var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);

            byte[] buffer = new byte[131072];
            int read;
            long lastReportTick = Environment.TickCount64;

            while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;

                long now = Environment.TickCount64;
                if (now - lastReportTick >= 100 || totalRead == totalBytes)
                {
                    lastReportTick = now;
                    double pct = Math.Min(99.0, (double)totalRead / totalBytes * 100.0);
                    string mbStr = $"{totalRead / (1024.0 * 1024.0):F1} MB / {totalBytes / (1024.0 * 1024.0):F1} MB ({pct:F0}%)";
                    progress?.Report(($"Downloading {package.DisplayName}...", pct, mbStr));
                }
            }
        }

        if (File.Exists(targetFile))
        {
            File.Delete(targetFile);
        }

        File.Move(tempFile, targetFile);
        progress?.Report(("Model downloaded successfully!", 100, package.FileName));
        return targetFile;
    }

    public async Task<bool> StartServerAsync(string modelFileName, int port = 9931, CancellationToken ct = default)
    {
        StopServer();
        LastServerError = null;

        string? exe = FindInstalledServerExecutable();
        if (exe is null)
        {
            LastServerError = "llama-server.exe was not found. Please click Auto Setup to install the runtime.";
            return false;
        }

        string modelPath = Path.IsPathRooted(modelFileName)
            ? modelFileName
            : Path.Combine(_modelsDir, modelFileName);

        if (!File.Exists(modelPath))
        {
            LastServerError = $"Model file '{modelFileName}' not found. Please download it via Auto Setup.";
            return false;
        }

        string exeDir = Path.GetDirectoryName(exe) ?? _binDir;
        int threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
        string args = $"-m \"{modelPath}\" --port {port} --host 127.0.0.1 -c 2048 -t {threads} --log-disable";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = exeDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.EnvironmentVariables["PATH"] = exeDir + ";" + currentPath;

        try
        {
            _serverProcess = Process.Start(psi);
            if (_serverProcess is null)
            {
                LastServerError = "Failed to launch llama-server process.";
                return false;
            }

            // Ping server health for up to 25 seconds until ready
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (int i = 0; i < 40; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    StopServer();
                    LastServerError = "Server startup was cancelled.";
                    return false;
                }

                if (_serverProcess.HasExited)
                {
                    string err = string.Empty;
                    try { err = _serverProcess.StandardError.ReadToEnd(); } catch { }
                    LastServerError = $"llama-server exited with code {_serverProcess.ExitCode}. {err}".Trim();
                    StopServer();
                    return false;
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
                try
                {
                    using var resp = await client.GetAsync($"http://127.0.0.1:{port}/health", ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (_serverProcess is not null && !_serverProcess.HasExited)
            {
                return true;
            }

            LastServerError = "Server startup timed out after 20 seconds.";
            StopServer();
            return false;
        }
        catch (Exception ex)
        {
            LastServerError = $"Error launching server: {ex.Message}";
            StopServer();
            return false;
        }
    }

    public void StopServer()
    {
        try
        {
            if (_serverProcess is not null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(1500);
            }
        }
        catch
        {
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
        }

        try
        {
            foreach (var p in Process.GetProcessesByName("llama-server"))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(1000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    public bool IsServerRunning()
    {
        if (_serverProcess is not null && !_serverProcess.HasExited)
        {
            return true;
        }

        try
        {
            if (Process.GetProcessesByName("llama-server").Length > 0)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public void Dispose()
    {
        StopServer();
    }
}

