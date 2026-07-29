using System;
using FileFormat.Core;

namespace FileFormat.AtariTxs;

/// <summary>In-memory representation of an Atari 8-bit .txs texture.</summary>
/// <remarks>
/// A six-byte header, then a 16x16 grid holding one GTIA colour value each. The values are hue 0,
/// so the picture is sixteen greys — a texture, not a picture, which is why it is stored so small
/// and drawn so large: each stored value covers a 4x4 block, giving 64x64 on screen.
/// </remarks>
[FormatMagicBytes([0xFF, 0xFF, 0x00, 0x06, 0xFF, 0x06])]
public readonly record struct AtariTxsFile
  : IImageFormatReader<AtariTxsFile>, IImageToRawImage<AtariTxsFile>,
    IImageFromRawImage<AtariTxsFile>, IImageFormatWriter<AtariTxsFile> {

  /// <summary>The fixed header, an Atari DOS load segment covering the 256 bytes that follow.</summary>
  public static ReadOnlySpan<byte> Header => [0xFF, 0xFF, 0x00, 0x06, 0xFF, 0x06];

  /// <summary>Stored values across and down.</summary>
  public const int StoredSize = 16;

  /// <summary>Screen pixels one stored value covers, in each direction.</summary>
  public const int Scale = 4;

  /// <summary>Displayed width and height.</summary>
  public const int DisplaySize = StoredSize * Scale;

  /// <summary>Colours a value may take.</summary>
  public const int ColorCount = 16;

  /// <summary>Total file size.</summary>
  public const int FileSize = 6 + StoredSize * StoredSize;

  static string IImageFormatMetadata<AtariTxsFile>.PrimaryExtension => ".txs";
  static string[] IImageFormatMetadata<AtariTxsFile>.FileExtensions => [".txs"];
  static AtariTxsFile IImageFormatReader<AtariTxsFile>.FromSpan(ReadOnlySpan<byte> data) => AtariTxsReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariTxsFile>.ToBytes(AtariTxsFile file) => AtariTxsWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariTxsFile>.VideoModes => [
    new("Texture", [(DisplaySize, DisplaySize)], [ColorCount])
  ];

  /// <summary>One GTIA colour value per stored cell, each 0..15.</summary>
  public byte[] Values { get; init; }

  /// <summary>The sixteen luminances of hue 0, which is what a value names.</summary>
  internal static byte[] PaletteRgb() => Atari8BitGraphics.Palette[..(ColorCount * 3)].ToArray();

  public static RawImage ToRawImage(AtariTxsFile file) {
    var values = file.Values ?? [];
    var pixels = new byte[DisplaySize * DisplaySize];

    for (var y = 0; y < DisplaySize; ++y)
    for (var x = 0; x < DisplaySize; ++x) {
      var cell = (y / Scale) * StoredSize + (x / Scale);
      pixels[y * DisplaySize + x] = (byte)(cell < values.Length ? values[cell] & 15 : 0);
    }

    return new() {
      Width = DisplaySize,
      Height = DisplaySize,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = PaletteRgb(),
      PaletteCount = ColorCount,
    };
  }

  public static AtariTxsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplaySize || image.Height != DisplaySize)
      throw new ArgumentException($"Expected {DisplaySize}x{DisplaySize} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var palette = PaletteRgb();
    var values = new byte[StoredSize * StoredSize];

    // One value covers a 4x4 block, so the block's average decides it — and since the sixteen
    // colours are the greys of hue 0, that reduces to picking the nearest luminance.
    for (var cellY = 0; cellY < StoredSize; ++cellY)
    for (var cellX = 0; cellX < StoredSize; ++cellX) {
      int red = 0, green = 0, blue = 0;
      for (var y = 0; y < Scale; ++y)
      for (var x = 0; x < Scale; ++x) {
        var pixel = ((cellY * Scale + y) * DisplaySize + cellX * Scale + x) * 4;
        red += bgra.PixelData[pixel + 2];
        green += bgra.PixelData[pixel + 1];
        blue += bgra.PixelData[pixel];
      }

      const int count = Scale * Scale;
      values[cellY * StoredSize + cellX] =
        _Nearest(palette, (byte)(red / count), (byte)(green / count), (byte)(blue / count));
    }

    return new() { Values = values };
  }

  private static byte _Nearest(ReadOnlySpan<byte> palette, byte red, byte green, byte blue) {
    var best = (byte)0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < ColorCount; ++i) {
      int dr = palette[i * 3] - red, dg = palette[i * 3 + 1] - green, db = palette[i * 3 + 2] - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = (byte)i;
    }

    return best;
  }
}
