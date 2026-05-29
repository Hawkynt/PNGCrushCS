using System;
using FileFormat.Core;

namespace FileFormat.C16Plus4;

/// <summary>In-memory representation of a Commodore 16/Plus4 multicolor screen image.</summary>
public readonly record struct C16Plus4File : IImageFormatReader<C16Plus4File>, IImageToRawImage<C16Plus4File>, IImageFromRawImage<C16Plus4File>, IImageFormatWriter<C16Plus4File> {

  internal const int FixedWidth = 160;
  internal const int FixedHeight = 200;
  internal const int FileSize = 10003;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFormatMetadata<C16Plus4File>.PrimaryExtension => ".c16";
  static string[] IImageFormatMetadata<C16Plus4File>.FileExtensions => [".c16", ".plus4"];
  static C16Plus4File IImageFormatReader<C16Plus4File>.FromSpan(ReadOnlySpan<byte> data) => C16Plus4Reader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<C16Plus4File>.Capabilities => FormatCapability.IndexedOnly;
  static IntegerRange[] IImageFormatMetadata<C16Plus4File>.AllowedPaletteRanges => [16];
  static FixedPalette[] IImageFormatMetadata<C16Plus4File>.FixedPalettes => _FixedPalettes;
  private static readonly FixedPalette[] _FixedPalettes = [
    new FixedPalette("C16/Plus4 (default)",
      0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
      0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF)
  ];
  static byte[] IImageFormatWriter<C16Plus4File>.ToBytes(C16Plus4File file) => C16Plus4Writer.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(C16Plus4File file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = _DefaultPalette[..],
      PaletteCount = 16,
    };
  }

  public static C16Plus4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
