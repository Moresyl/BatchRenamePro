using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.Services;

/// <summary>Opens trusted web links in the user's default browser.</summary>
public interface IExternalLauncher
{
    /// <summary>Opens an absolute HTTP or HTTPS address.</summary>
    /// <returns><see langword="true"/> when Windows accepted the request.</returns>
    bool Open(string? url);
}

/// <inheritdoc cref="IExternalLauncher" />
public sealed class ExternalLauncher(ILogger<ExternalLauncher> logger) : IExternalLauncher
{
    /// <inheritdoc />
    public bool Open(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception error) when (error is IOException or Win32Exception)
        {
            logger.LogWarning(error, "Could not open {Url}", uri);
            return false;
        }
    }
}
