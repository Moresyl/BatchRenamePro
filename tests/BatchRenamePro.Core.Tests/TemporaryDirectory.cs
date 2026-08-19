namespace BatchRenamePro.Core.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BatchRenameProTests-{Guid.NewGuid():N}");

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public string CreateFile(string name, string content = "")
    {
        var path = System.IO.Path.Combine(Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
