using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BatchRenamePro.App.Services;

/// <summary>The current phase of downloading and handing an update to Windows Installer.</summary>
public enum UpdateInstallStage
{
    /// <summary>The MSI is being downloaded.</summary>
    Downloading,

    /// <summary>The downloaded MSI is being checked against the release checksum.</summary>
    Verifying,

    /// <summary>The detached installer helper is being started.</summary>
    StartingInstaller
}

/// <summary>Progress reported while preparing a one-click update.</summary>
/// <param name="Stage">The current update phase.</param>
/// <param name="Percentage">Download completion from 0 through 100.</param>
public sealed record UpdateInstallProgress(UpdateInstallStage Stage, int Percentage);

/// <summary>Downloads, verifies and starts the installer for a published update.</summary>
public interface IUpdateInstaller
{
    /// <summary>Prepares the architecture-matched MSI and starts its detached upgrade helper.</summary>
    Task DownloadAndLaunchAsync(
        AppUpdateInfo update,
        IProgress<UpdateInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A Windows MSI updater backed by the project's trusted GitHub Release assets.</summary>
public sealed class WindowsUpdateInstaller : IUpdateInstaller, IDisposable
{
    private const long MaximumInstallerBytes = 256L * 1024 * 1024;
    private const int BufferSize = 128 * 1024;

    private readonly HttpClient _client;
    private readonly string _repositoryUrl;
    private readonly Func<PreparedUpdate, bool> _launch;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>Creates an updater with a dedicated long-running download client.</summary>
    public WindowsUpdateInstaller(string repositoryUrl)
        : this(
            new HttpClient { Timeout = TimeSpan.FromMinutes(30) },
            repositoryUrl,
            LaunchHelper,
            ownsClient: true)
    {
    }

    internal WindowsUpdateInstaller(
        HttpClient client,
        string repositoryUrl,
        Func<PreparedUpdate, bool> launch,
        bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentNullException.ThrowIfNull(launch);

        if (!ProductLinks.TryGetGitHubRepository(repositoryUrl, out _, out _) ||
            ProductLinks.IsPlaceholderRepository(repositoryUrl))
        {
            throw new ArgumentException("A canonical public GitHub repository is required.", nameof(repositoryUrl));
        }

        _client = client;
        _repositoryUrl = repositoryUrl.TrimEnd('/');
        _launch = launch;
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    public async Task DownloadAndLaunchAsync(
        AppUpdateInfo update,
        IProgress<UpdateInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(update);

        var runtimeIdentifier = GetRuntimeIdentifier(RuntimeInformation.ProcessArchitecture);
        var installerName = $"BatchRenamePro-{runtimeIdentifier}.msi";
        if (!Version.TryParse(update.Version, out _) || string.IsNullOrWhiteSpace(update.TagName))
            throw new InvalidDataException("The update metadata contains an invalid release identity.");

        var escapedTag = string.Join('/', update.TagName.Split('/').Select(Uri.EscapeDataString));
        var checksumUri = new Uri($"{_repositoryUrl}/releases/download/{escapedTag}/SHA256SUMS.txt");
        var installerUri = new Uri($"{_repositoryUrl}/releases/download/{escapedTag}/{installerName}");

        var checksumManifest = await DownloadChecksumManifestAsync(checksumUri, cancellationToken).ConfigureAwait(false);
        var expectedHash = ParseChecksum(checksumManifest, installerName);
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "BatchRenamePro",
            "Updates",
            update.Version + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, installerName);
        try
        {
            await DownloadInstallerAsync(installerUri, installerPath, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(UpdateInstallStage.Verifying, 100));

            await using (var stream = new FileStream(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(false));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The downloaded installer failed its SHA-256 verification.");
                }
            }

            progress?.Report(new(UpdateInstallStage.StartingInstaller, 100));
            var prepared = PrepareLaunch(installerPath, updateDirectory);
            if (!_launch(prepared)) throw new InvalidOperationException("The Windows update helper could not be started.");
        }
        catch
        {
            TryDeleteDirectory(updateDirectory);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient) _client.Dispose();
    }

    internal static string GetRuntimeIdentifier(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => throw new PlatformNotSupportedException($"Windows architecture '{architecture}' is not supported.")
    };

    internal static string ParseChecksum(string manifest, string installerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerName);

        string? match = null;
        foreach (var line in manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || !fields[1].TrimStart('*').Equals(installerName, StringComparison.Ordinal)) continue;
            if (match is not null) throw new InvalidDataException($"The checksum manifest lists {installerName} more than once.");
            match = fields[0];
        }

        if (match is null || match.Length != 64 || match.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"The checksum manifest does not contain a valid SHA-256 for {installerName}.");

        return match.ToUpperInvariant();
    }

    private static PreparedUpdate PrepareLaunch(string installerPath, string updateDirectory)
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        var currentDirectory = Path.GetDirectoryName(currentExecutable)
            ?? throw new InvalidOperationException("The current executable directory is unavailable.");
        var installedProductDirectory = Path.GetFileName(currentDirectory).Equals(
            "Batch Rename Pro",
            StringComparison.OrdinalIgnoreCase);
        var installRoot = installedProductDirectory ? Directory.GetParent(currentDirectory)?.FullName : null;
        var defaultRoot = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var targetRoot = installRoot ?? defaultRoot;
        var targetExecutable = Path.Combine(targetRoot, "Batch Rename Pro", "BatchRenamePro.exe");
        var helperPath = Path.Combine(updateDirectory, "install-update.ps1");

        File.WriteAllText(helperPath, HelperScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new PreparedUpdate(
            helperPath,
            installerPath,
            installRoot,
            targetExecutable,
            currentExecutable,
            Environment.ProcessId,
            updateDirectory);
    }

    private async Task<string> DownloadChecksumManifestAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 64 * 1024)
            throw new InvalidDataException("The checksum manifest is unexpectedly large.");

        var manifest = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (Encoding.UTF8.GetByteCount(manifest) > 64 * 1024)
            throw new InvalidDataException("The checksum manifest is unexpectedly large.");
        return manifest;
    }

    private async Task DownloadInstallerAsync(
        Uri uri,
        string destination,
        IProgress<UpdateInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var expectedLength = response.Content.Headers.ContentLength;
        if (expectedLength is > MaximumInstallerBytes)
            throw new InvalidDataException("The installer is unexpectedly large.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destinationStream = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long downloaded = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            downloaded += read;
            if (downloaded > MaximumInstallerBytes) throw new InvalidDataException("The installer is unexpectedly large.");
            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            var percentage = expectedLength is > 0
                ? (int)Math.Clamp(downloaded * 100 / expectedLength.Value, 0, 100)
                : 0;
            progress?.Report(new(UpdateInstallStage.Downloading, percentage));
        }
    }

    private static bool LaunchHelper(PreparedUpdate prepared)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
            "-File", prepared.HelperPath,
            "-ParentProcessId", prepared.ParentProcessId.ToString(CultureInfo.InvariantCulture),
            "-InstallerBase64", Encode(prepared.InstallerPath),
            "-InstallRootBase64", Encode(prepared.InstallRoot ?? string.Empty),
            "-TargetBase64", Encode(prepared.TargetExecutable),
            "-FallbackBase64", Encode(prepared.CurrentExecutable),
            "-UpdateDirectoryBase64", Encode(prepared.UpdateDirectory)
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        return process is not null;
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // The original update exception is more useful than a best-effort cleanup failure.
        }
        catch (UnauthorizedAccessException)
        {
            // The original update exception is more useful than a best-effort cleanup failure.
        }
    }

    internal sealed record PreparedUpdate(
        string HelperPath,
        string InstallerPath,
        string? InstallRoot,
        string TargetExecutable,
        string CurrentExecutable,
        int ParentProcessId,
        string UpdateDirectory);

    private const string HelperScript = """
        param(
            [Parameter(Mandatory = $true)][int] $ParentProcessId,
            [Parameter(Mandatory = $true)][string] $InstallerBase64,
            [Parameter(Mandatory = $true)][string] $InstallRootBase64,
            [Parameter(Mandatory = $true)][string] $TargetBase64,
            [Parameter(Mandatory = $true)][string] $FallbackBase64,
            [Parameter(Mandatory = $true)][string] $UpdateDirectoryBase64
        )
        $ErrorActionPreference = 'Stop'
        function Decode([string] $value) {
            [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
        }
        $installer = Decode $InstallerBase64
        $installRoot = Decode $InstallRootBase64
        $target = Decode $TargetBase64
        $fallback = Decode $FallbackBase64
        $updateDirectory = Decode $UpdateDirectoryBase64
        try {
            Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
            $arguments = "/i `"$installer`" /passive /norestart"
            if ($installRoot) { $arguments += " INSTALLROOT=`"$installRoot`"" }
            $installerProcess = Start-Process -FilePath 'msiexec.exe' -Verb RunAs -ArgumentList $arguments -PassThru
            $installerProcess.WaitForExit()
            $restart = if ($installerProcess.ExitCode -in 0, 3010 -and (Test-Path -LiteralPath $target)) { $target } else { $fallback }
            if (Test-Path -LiteralPath $restart) { Start-Process -FilePath $restart }
        }
        catch {
            if (Test-Path -LiteralPath $fallback) { Start-Process -FilePath $fallback }
        }
        finally {
            Start-Sleep -Milliseconds 500
            Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $updateDirectory -Force -ErrorAction SilentlyContinue
        }
        """;
}
