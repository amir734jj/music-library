namespace MusicLibrary.Api.Services;

internal static class TrackAudioValidation
{
    public static bool TryAnalyze(byte[] content, out TrackAudioInfo audioInfo)
    {
        audioInfo = new TrackAudioInfo(string.Empty, 0);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var track = new ATL.Track(stream);
            if (track.DurationMs <= 0 || track.SampleRate <= 0 || track.Bitrate <= 0) return false;

            var detectedType = track.AudioFormat.MimeList.FirstOrDefault()?.ToLowerInvariant();
            var contentType = detectedType switch
            {
                "audio/aac" or "audio/aacp" => "audio/aac",
                "audio/flac" or "audio/x-flac" => "audio/flac",
                "audio/ogg" or "application/ogg" => "audio/ogg",
                "audio/mp3" or "audio/mpeg" => "audio/mpeg",
                _ => string.Empty
            };
            if (contentType.Length == 0) return false;

            audioInfo = new TrackAudioInfo(contentType, Convert.ToInt32(Math.Round(Convert.ToDouble(track.Bitrate))));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}