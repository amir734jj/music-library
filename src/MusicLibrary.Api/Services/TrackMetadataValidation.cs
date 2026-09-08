namespace MusicLibrary.Api.Services;

internal static class TrackMetadataValidation
{
    public static bool IsMeaningful(string? artist, string? title) =>
        ContainsLetterOrDigit(artist) || ContainsLetterOrDigit(title);

    private static bool ContainsLetterOrDigit(string? value) =>
        value?.Any(char.IsLetterOrDigit) == true;
}