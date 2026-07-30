using System;
using FileFormat.Core;

namespace FileFormat.InterlaceLogoDesigner;

/// <summary>In-memory representation of an Interlace Logo Designer picture (.ild).</summary>
/// <remarks>
/// Two Graphics 15 fields shown alternately and averaged by the eye. Unlike the editors that store
/// a register set per field, this one fixes both to the same three playfield colours on black, so
/// what interlacing buys here is not more colours but the mixtures between the four it has —
/// enough for a logo, which is all the program was for.
/// </remarks>
public readonly record struct InterlaceLogoDesignerFile
  : IImageFormatReader<InterlaceLogoDesignerFile>, IImageToRawImage<InterlaceLogoDesignerFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 128;

  /// <summary>Bytes one row occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Offset of the first field.</summary>
  public const int FirstFieldOffset = 0;

  /// <summary>Offset of the second field, which starts on the next four-kilobyte page.</summary>
  public const int SecondFieldOffset = 4096;

  /// <summary>Total file size.</summary>
  public const int FileSize = 8195;

  /// <summary>The registers both fields draw from: background, PF0, PF1 and PF2.</summary>
  public static ReadOnlySpan<byte> Registers => [0, 6, 2, 10];

  static string IImageFormatMetadata<InterlaceLogoDesignerFile>.PrimaryExtension => ".ild";
  static string[] IImageFormatMetadata<InterlaceLogoDesignerFile>.FileExtensions => [".ild"];
  static InterlaceLogoDesignerFile IImageFormatReader<InterlaceLogoDesignerFile>.FromSpan(ReadOnlySpan<byte> data)
    => InterlaceLogoDesignerReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<InterlaceLogoDesignerFile>.VideoModes => [
    new("Interlace Logo Designer", [(Width, Height)], [10])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(InterlaceLogoDesignerFile file) {
    var data = file.Data ?? [];

    var first = Atari8BitGraphics.DecodeGr15Frame(data, FirstFieldOffset, Stride, Width, Height, Registers);
    var second = Atari8BitGraphics.DecodeGr15Frame(data, SecondFieldOffset, Stride, Width, Height, Registers);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }
}
