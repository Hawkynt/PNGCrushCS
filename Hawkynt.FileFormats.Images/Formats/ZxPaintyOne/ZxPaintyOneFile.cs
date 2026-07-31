using System;
using FileFormat.Core;

namespace FileFormat.ZxPaintyOne;

/// <summary>In-memory representation of a ZXpaintyONE picture (.zp1).</summary>
/// <remarks>
/// A ZX81 screen written as hexadecimal text, two characters to a character code and nothing else —
/// no header, no separators, no line breaks. It is a screen dump made printable, the same idea as
/// the bulletin-board formats but without their alphabet: plain hexadecimal was enough because
/// nothing was expected to carry it but a file.
/// </remarks>
public readonly record struct ZxPaintyOneFile
  : IImageFormatReader<ZxPaintyOneFile>, IImageToRawImage<ZxPaintyOneFile> {

  static string IImageFormatMetadata<ZxPaintyOneFile>.PrimaryExtension => ".zp1";
  static string[] IImageFormatMetadata<ZxPaintyOneFile>.FileExtensions => [".zp1"];
  static ZxPaintyOneFile IImageFormatReader<ZxPaintyOneFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxPaintyOneReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ZxPaintyOneFile>.VideoModes => [
    new("ZX81", [(Zx81Graphics.Width, Zx81Graphics.Height)], [2])
  ];

  /// <summary>The screen's character codes.</summary>
  public byte[] Screen { get; init; }

  public static RawImage ToRawImage(ZxPaintyOneFile file) => new() {
    Width = Zx81Graphics.Width,
    Height = Zx81Graphics.Height,
    Format = PixelFormat.Indexed8,
    PixelData = Zx81Graphics.Decode(file.Screen ?? []),
    Palette = Zx81Graphics.CreatePalette(),
    PaletteCount = 2,
  };
}
