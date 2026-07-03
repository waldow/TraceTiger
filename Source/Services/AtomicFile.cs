using System.IO;

namespace TraceTiger.Services;

// Writes files via a temp file + atomic rename so a crash mid-write can never
// leave a half-written (corrupt) file at the real path.
internal static class AtomicFile
{
    public static async Task WriteAllTextAsync(string path, string contents)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, contents).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public static void WriteAllText(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }
}
