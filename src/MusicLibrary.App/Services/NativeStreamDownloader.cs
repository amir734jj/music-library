namespace MusicLibrary.App.Services;

public static class NativeStreamDownloader
{
    public static async Task<string> SaveAsync(
        byte[] content,
        string fileName,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "radio-track.mp3";
        var existingFile = new FileInfo(Path.Combine(destinationDirectory, safeFileName));
        if (existingFile.Exists && existingFile.Length == content.LongLength) return existingFile.FullName;

        var temporaryPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.download");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            var destinationPath = GetAvailableDestinationPath(destinationDirectory, safeFileName);
            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string GetAvailableDestinationPath(string directory, string fileName)
    {
        var destinationPath = Path.Combine(directory, fileName);
        if (!File.Exists(destinationPath)) return destinationPath;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; ; suffix++)
        {
            destinationPath = Path.Combine(directory, $"{name} ({suffix}){extension}");
            if (!File.Exists(destinationPath)) return destinationPath;
        }
    }
}