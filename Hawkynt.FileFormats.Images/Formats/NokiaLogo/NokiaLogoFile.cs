using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NokiaLogo;

/// <summary>In-memory representation of a Nokia Operator Logo image.</summary>
/// <remarks>
/// Twenty bytes of header, then one character a pixel — the letter zero or the letter one, in ASCII,
/// rather than a bit. It is an extravagant way to store a two-colour picture and it is what the
/// format does: a phone's logo was small enough that nobody minded, and a text body survived being
/// mailed about in ways a binary one did not.
/// <para/>
/// What was here before was the bare bitmap at one bit a pixel, fixed at 72 by 14, with no header at
/// all — neither the layout nor the size restriction is real.
/// </remarks>
public readonly record struct NokiaLogoFile
  : IImageFormatReader<NokiaLogoFile>, IImageToRawImage<NokiaLogoFile>,
    IImageFromRawImage<NokiaLogoFile>, IImageFormatWriter<NokiaLogoFile> {

  /// <summary>Bytes before the picture.</summary>
  internal const int HeaderSize = 20;

  /// <summary>The three letters every file starts with.</summary>
  internal const string Signature = "NOL";

  /// <summary>Where the size sits in the header.</summary>
  internal const int WidthOffset = 10;

  internal const int HeightOffset = 12;

  // Ink first, which is the opposite way round from its neighbours: the tool that reads these draws
  // a clear bit black, every pixel of the sample.
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<NokiaLogoFile>.PrimaryExtension => ".nol";
  static string[] IImageFormatMetadata<NokiaLogoFile>.FileExtensions => [".nol", ".ngg"];
  static NokiaLogoFile IImageFormatReader<NokiaLogoFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaLogoReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<NokiaLogoFile>.VideoModes => [
    new("Operator logo", [(72, 14)], [2]),
    new("Any", [(IntegerRange.Any, IntegerRange.Any)], [2]),
  ];
  static byte[] IImageFormatWriter<NokiaLogoFile>.ToBytes(NokiaLogoFile file) => NokiaLogoWriter.ToBytes(file);

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>One index a pixel, zero for paper and one for ink.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(NokiaLogoFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static NokiaLogoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Threshold(image, setWhenDark: true),
    };
  }
}
