using System;
using FileFormat.Core;

namespace FileFormat.Anime4Ever;

/// <summary>In-memory representation of an Anime 4ever picture (.a4r).</summary>
/// <remarks>
/// A Graphics 9 screen — sixteen luminances of one hue, 320 by 256 — packed with a dictionary coder
/// that writes to an address rather than a position. The stream begins with nowhere to write and
/// only a command naming a destination lets it start, which is what a packer built to load straight
/// into video memory produces: it is describing where the bytes go, not what order they come in.
/// </remarks>
public readonly record struct Anime4EverFile
  : IImageFormatReader<Anime4EverFile>, IImageToRawImage<Anime4EverFile>,
    IImageFromRawImage<Anime4EverFile>, IImageFormatWriter<Anime4EverFile> {

  static byte[] IImageFormatWriter<Anime4EverFile>.ToBytes(Anime4EverFile file) => Anime4EverWriter.ToBytes(file);

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 256;

  /// <summary>Bytes one row occupies: a nibble per four pixels.</summary>
  public const int Stride = Width / 8;

  /// <summary>Offset of the bitmap within what the stream writes.</summary>
  public const int BitmapOffset = 512;

  /// <summary>Bytes the stream can address.</summary>
  public const int UnpackedSize = 11248;

  static string IImageFormatMetadata<Anime4EverFile>.PrimaryExtension => ".a4r";
  static string[] IImageFormatMetadata<Anime4EverFile>.FileExtensions => [".a4r"];
  static Anime4EverFile IImageFormatReader<Anime4EverFile>.FromSpan(ReadOnlySpan<byte> data)
    => Anime4EverReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Anime4EverFile>.VideoModes => [
    new("Anime 4ever", [(Width, Height)], [16])
  ];

  /// <summary>The unpacked memory image.</summary>
  public byte[] Unpacked { get; init; }

  public static RawImage ToRawImage(Anime4EverFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Rgb24,
    PixelData = Atari8BitGraphics.DecodeGr9Frame(file.Unpacked ?? [], BitmapOffset, Stride, Width, Height, 0, 0),
  };

  /// <summary>Builds a picture in Graphics 9, which is sixteen luminances of one hue.</summary>
  public static Anime4EverFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var luminance = new byte[Width * Height];
    for (var i = 0; i < luminance.Length; ++i) {
      var at = i * 3;
      luminance[i] = (byte)((rgb[at] * 77 + rgb[at + 1] * 150 + rgb[at + 2] * 29) >> 12);
    }

    var unpacked = new byte[UnpackedSize];
    Atari8BitGraphics.PackGr9(luminance, Width, Height).AsSpan(0, Math.Min(Stride * Height, UnpackedSize - BitmapOffset))
      .CopyTo(unpacked.AsSpan(BitmapOffset));

    return new() { Unpacked = unpacked };
  }
}
