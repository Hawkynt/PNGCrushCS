using System;
using FileFormat.Core;

namespace FileFormat.VertiZontalInterlacing;

/// <summary>In-memory representation of a VertiZontal Interlacing picture (.vzi).</summary>
/// <remarks>
/// Two Graphics 9 screens shown on alternate television fields, displaced two pixels from each
/// other. Graphics 9 gives sixteen luminances of one hue but spends a nibble on every four screen
/// pixels, so the mode is far coarser horizontally than vertically. Offsetting the fields is what
/// the name refers to: averaged, the pair resolves edges the four-pixel nibbles cannot, and doubles
/// the luminance steps at the same time.
/// </remarks>
public readonly record struct VertiZontalInterlacingFile
  : IImageFormatReader<VertiZontalInterlacingFile>, IImageToRawImage<VertiZontalInterlacingFile>,
    IImageFromRawImage<VertiZontalInterlacingFile>, IImageFormatWriter<VertiZontalInterlacingFile> {

  static byte[] IImageFormatWriter<VertiZontalInterlacingFile>.ToBytes(VertiZontalInterlacingFile file)
    => VertiZontalInterlacingWriter.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Bytes one row occupies: a nibble per four pixels.</summary>
  public const int Stride = Width / 8;

  /// <summary>Offset of the first field.</summary>
  public const int FirstFieldOffset = 0;

  /// <summary>Offset of the second field.</summary>
  public const int SecondFieldOffset = Stride * Height;

  /// <summary>Total file size.</summary>
  public const int FileSize = SecondFieldOffset * 2;

  static string IImageFormatMetadata<VertiZontalInterlacingFile>.PrimaryExtension => ".vzi";
  static string[] IImageFormatMetadata<VertiZontalInterlacingFile>.FileExtensions => [".vzi"];
  static VertiZontalInterlacingFile IImageFormatReader<VertiZontalInterlacingFile>.FromSpan(ReadOnlySpan<byte> data)
    => VertiZontalInterlacingReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<VertiZontalInterlacingFile>.VideoModes => [
    new("VertiZontal Interlacing", [(Width, Height)], [16])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(VertiZontalInterlacingFile file) {
    var data = file.Data ?? [];

    // The fields sit a pixel either side of centre, which is what displaces them two apart.
    var first = Atari8BitGraphics.DecodeGr9Frame(data, FirstFieldOffset, Stride, Width, Height, 0, -1);
    var second = Atari8BitGraphics.DecodeGr9Frame(data, SecondFieldOffset, Stride, Width, Height, 0, 1);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Builds a picture, putting the same field in both halves.</summary>
  /// <remarks>
  /// Graphics 9 has sixteen luminances of one hue and no colour at all, so the picture is reduced to
  /// its brightness. Both fields hold the same rows.
  /// <para/>
  /// The two are read a pixel either side of centre, which is the whole point of the mode — the
  /// displacement is what puts detail between the stored positions. Writing them identically means
  /// each output pixel averages the two stored positions next to it, so the result comes back very
  /// slightly softened across every fourth column rather than exactly as written. Recovering a pair
  /// of fields that average to a given picture is not determined by that picture, and guessing one
  /// would be inventing detail.
  /// </remarks>
  public static VertiZontalInterlacingFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var luminance = new byte[Width * Height];
    for (var i = 0; i < luminance.Length; ++i) {
      var at = i * 3;
      luminance[i] = (byte)((rgb.PixelData[at] * 77 + rgb.PixelData[at + 1] * 150 + rgb.PixelData[at + 2] * 29) >> 12);
    }

    var field = Atari8BitGraphics.PackGr9(luminance, Width, Height);
    var data = new byte[FileSize];
    field.AsSpan(0, Math.Min(field.Length, SecondFieldOffset)).CopyTo(data.AsSpan(FirstFieldOffset));
    field.AsSpan(0, Math.Min(field.Length, SecondFieldOffset)).CopyTo(data.AsSpan(SecondFieldOffset));

    return new() { Data = data };
  }
}
