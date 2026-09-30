namespace BasicApi.Services.Media;

/// <summary>
/// What a file really is, by its first bytes. The type the client claims is not trusted: it
/// decides only the name shown, never how the file is served or processed.
/// </summary>
public static class MediaSniffer
{
    /// <summary>How many first bytes <see cref="Sniff"/> looks at.</summary>
    public const int HeaderLength = 64;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Gif = "image/gif";
    public const string Webp = "image/webp";
    public const string Mp4 = "video/mp4";
    public const string QuickTime = "video/quicktime";
    public const string Webm = "video/webm";
    public const string Ogg = "audio/ogg";
    public const string Mpeg = "audio/mpeg";
    public const string M4a = "audio/mp4";
    public const string Unknown = "application/octet-stream";

    /// <summary>A picture the server can decode and make a preview of.</summary>
    public static bool IsImage(string mime) => mime is Jpeg or Png or Gif or Webp;

    public static bool IsVideo(string mime) => mime is Mp4 or QuickTime or Webm;

    /// <summary>Formats a voice message comes in: Opus in Ogg or WebM, AAC in MP4, MP3.</summary>
    public static bool IsAudio(string mime) => mime is Ogg or Mpeg or M4a or Webm or Mp4;

    public static string Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return Jpeg;
        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return Png;
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
            return Gif;
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return Webp;
        if (head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return Webm; // Matroska/WebM: video or Opus audio alike
        if (head.StartsWith("OggS"u8))
            return Ogg;
        if (head.StartsWith("ID3"u8) || (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0))
            return Mpeg;
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = head[8..12];
            if (brand.SequenceEqual("qt  "u8))
                return QuickTime;
            if (brand.SequenceEqual("M4A "u8) || brand.SequenceEqual("M4B "u8))
                return M4a;
            return Mp4;
        }
        return Unknown;
    }
}
