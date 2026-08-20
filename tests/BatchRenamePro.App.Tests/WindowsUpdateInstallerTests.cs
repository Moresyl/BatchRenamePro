using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BatchRenamePro.App.Services;

namespace BatchRenamePro.App.Tests;

[TestClass]
public sealed class WindowsUpdateInstallerTests
{
    [TestMethod]
    [DataRow(Architecture.X64, "win-x64")]
    [DataRow(Architecture.X86, "win-x86")]
    [DataRow(Architecture.Arm64, "win-arm64")]
    public void GetRuntimeIdentifier_SupportedArchitecture_ReturnsReleaseName(
        Architecture architecture,
        string expected)
    {
        Assert.AreEqual(expected, WindowsUpdateInstaller.GetRuntimeIdentifier(architecture));
    }

    [TestMethod]
    public void GetRuntimeIdentifier_UnsupportedArchitecture_FailsClosed()
    {
        Assert.ThrowsExactly<PlatformNotSupportedException>(() =>
            WindowsUpdateInstaller.GetRuntimeIdentifier(Architecture.Arm));
    }

    [TestMethod]
    public void ParseChecksum_RequiresOneExactValidAsset()
    {
        const string expected = "8D969EEF6ECAD3C29A3A629280E686C3F5D5A86AFF3CA12020C923ADC6C92B6D";
        var manifest = $"{expected.ToLowerInvariant()}  BatchRenamePro-win-x64.msi\n";

        Assert.AreEqual(expected, WindowsUpdateInstaller.ParseChecksum(manifest, "BatchRenamePro-win-x64.msi"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            WindowsUpdateInstaller.ParseChecksum(manifest, "BatchRenamePro-win-x86.msi"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            WindowsUpdateInstaller.ParseChecksum(manifest + manifest, "BatchRenamePro-win-x64.msi"));
    }

    [TestMethod]
    public async Task DownloadAndLaunchAsync_UsesExactTagArchitectureAndVerifiedMsi()
    {
        var installerBytes = Encoding.UTF8.GetBytes("test MSI payload");
        var installerName = $"BatchRenamePro-{WindowsUpdateInstaller.GetRuntimeIdentifier(RuntimeInformation.ProcessArchitecture)}.msi";
        var hash = Convert.ToHexString(SHA256.HashData(installerBytes));
        var requested = new List<string>();
        var handler = new DelegateHandler(request =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? Response(Encoding.UTF8.GetBytes($"{hash}  {installerName}\n"), "text/plain")
                : Response(installerBytes, "application/octet-stream");
        });
        using var client = new HttpClient(handler);
        WindowsUpdateInstaller.PreparedUpdate? prepared = null;
        using var updater = new WindowsUpdateInstaller(
            client,
            "https://github.com/Moresyl/BatchRenamePro",
            value =>
            {
                prepared = value;
                return true;
            });

        try
        {
            await updater.DownloadAndLaunchAsync(Update("v2.0.4"));

            Assert.IsNotNull(prepared);
            CollectionAssert.AreEqual(
            new[]
            {
                "https://github.com/Moresyl/BatchRenamePro/releases/download/v2.0.4/SHA256SUMS.txt",
                $"https://github.com/Moresyl/BatchRenamePro/releases/download/v2.0.4/{installerName}"
            }, requested);
            CollectionAssert.AreEqual(installerBytes, await File.ReadAllBytesAsync(prepared.InstallerPath));
            var helper = await File.ReadAllTextAsync(prepared.HelperPath);
            StringAssert.Contains(helper, "Wait-Process");
            StringAssert.Contains(helper, "msiexec.exe");
            StringAssert.Contains(helper, "-Verb RunAs");
        }
        finally
        {
            if (prepared is not null && Directory.Exists(prepared.UpdateDirectory))
                Directory.Delete(prepared.UpdateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadAndLaunchAsync_ChecksumMismatch_DoesNotStartInstaller()
    {
        var handler = new DelegateHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Response(Encoding.UTF8.GetBytes($"{new string('0', 64)}  {CurrentInstallerName()}\n"), "text/plain")
            : Response(Encoding.UTF8.GetBytes("tampered"), "application/octet-stream"));
        using var client = new HttpClient(handler);
        var launched = false;
        using var updater = new WindowsUpdateInstaller(
            client,
            "https://github.com/Moresyl/BatchRenamePro",
            _ => launched = true);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            updater.DownloadAndLaunchAsync(Update("v2.0.4")));

        Assert.IsFalse(launched);
    }

    private static AppUpdateInfo Update(string tag) => new(
        "2.0.4",
        "Batch Rename Pro 2.0.4",
        "notes",
        DateTimeOffset.UtcNow,
        "https://github.com/Moresyl/BatchRenamePro/releases/tag/v2.0.4",
        tag);

    private static string CurrentInstallerName() =>
        $"BatchRenamePro-{WindowsUpdateInstaller.GetRuntimeIdentifier(RuntimeInformation.ProcessArchitecture)}.msi";

    private static HttpResponseMessage Response(byte[] content, string contentType) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content)
        {
            Headers = { ContentType = new(contentType) }
        }
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
