using System.Buffers.Binary;
using System.IO;
using System.Windows;

namespace BatchRenamePro.App.Interop;

/// <summary>Builds a Win32 icon handle from a .ico compiled into the assembly.</summary>
/// <remarks>
/// <para>
/// The notification area wants an icon at one specific size, and which size that is depends on the
/// display scale. Handing it the 32-pixel frame and letting it shrink that to 20 is what makes a
/// tray icon look like a screenshot of itself, so this picks the frame closest above the requested
/// size and lets Windows do the small step down from there.
/// </para>
/// <para>
/// It reads the icon out of the assembly rather than off the executable on disk, because a
/// single-file publish extracts to a temporary directory and <c>ExtractIconEx</c> against
/// <c>Environment.ProcessPath</c> is not dependable there. It also means running under
/// <c>dotnet run</c>, where the host process is dotnet.exe, still shows the right picture.
/// </para>
/// </remarks>
internal static class IconLoader
{
    /// <summary>The version field of an icon resource: 3.0, and it has never been anything else.</summary>
    private const uint IconResourceVersion = 0x00030000;

    private const uint DefaultColor = 0;
    private const int HeaderLength = 6;
    private const int EntryLength = 16;

    /// <summary>Loads a .ico resource and returns an icon handle at the requested size.</summary>
    /// <param name="resource">A pack URI naming a file with the <c>Resource</c> build action.</param>
    /// <param name="size">The desired width and height in physical pixels.</param>
    /// <returns>An icon handle the caller owns and must destroy, or zero if the icon is unusable.</returns>
    public static nint Load(Uri resource, int size)
    {
        ArgumentNullException.ThrowIfNull(resource);

        byte[] bytes;

        try
        {
            var info = Application.GetResourceStream(resource);
            if (info is null) return 0;

            using var stream = info.Stream;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (IOException)
        {
            // A missing or unreadable icon costs the window an ornament, not its ability to run.
            return 0;
        }

        return Create(bytes, size);
    }

    private static nint Create(byte[] bytes, int size)
    {
        var span = new ReadOnlySpan<byte>(bytes);

        if (span.Length < HeaderLength) return 0;
        if (BinaryPrimitives.ReadUInt16LittleEndian(span[2..]) != 1) return 0;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (count <= 0 || span.Length < HeaderLength + (count * EntryLength)) return 0;

        var smallestThatFits = -1;
        var smallestThatFitsWidth = int.MaxValue;
        var largest = -1;
        var largestWidth = -1;

        for (var i = 0; i < count; i++)
        {
            // A width byte of zero means 256: the field is one byte and 256 does not fit in it.
            var entry = span[(HeaderLength + (i * EntryLength))..];
            int width = entry[0] == 0 ? 256 : entry[0];

            if (width >= size && width < smallestThatFitsWidth)
            {
                smallestThatFits = i;
                smallestThatFitsWidth = width;
            }

            if (width > largestWidth)
            {
                largest = i;
                largestWidth = width;
            }
        }

        var best = smallestThatFits >= 0 ? smallestThatFits : largest;
        if (best < 0) return 0;

        var chosen = span[(HeaderLength + (best * EntryLength))..];
        var length = BinaryPrimitives.ReadUInt32LittleEndian(chosen[8..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(chosen[12..]);

        // The directory is written by whatever produced the file, so it is not trusted to be in
        // bounds: a truncated icon would otherwise be handed to Win32 as a pointer and a length.
        if (offset > (uint)bytes.Length || length > (uint)bytes.Length - offset || length == 0) return 0;

        return NativeMethods.CreateIconFromResourceEx(
            ref bytes[(int)offset],
            length,
            isIcon: true,
            IconResourceVersion,
            size,
            size,
            DefaultColor);
    }
}
