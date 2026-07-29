using System;
using FileFormat.Core;

namespace FileFormat.Mamut;

/// <summary>In-memory representation of an Atari 8-bit Mamut (.bkg) screen.</summary>
/// <remarks>
/// A bare 3840-byte ANTIC mode D ("Graphics 7") bitmap with no colour information of its own —
/// Mamut leaves the screen on the operating system's default register values. The 160x96 logical
/// pixels are displayed at 320x192.
/// </remarks>
public readonly record struct MamutFile
  : IImageFormatReader<MamutFile>, IImageToRawImage<MamutFile>,
    IImageFromRawImage<MamutFile>, IImageFormatWriter<MamutFile> {

  /// <summary>Logical bitmap width.</summary>
  public const int BitmapWidth = Atari8BitGraphics.Gr7Width;

  /// <summary>Number of stored scanlines.</summary>
  public const int BitmapHeight = 96;

  /// <summary>Displayed width; each logical pixel is two screen pixels wide.</summary>
  public const int DisplayWidth = BitmapWidth * 2;

  /// <summary>Displayed height; each stored scanline is shown twice.</summary>
  public const int DisplayHeight = BitmapHeight * 2;

  /// <summary>Size of the bitmap section.</summary>
  public const int BitmapDataSize = Atari8BitGraphics.Gr7BytesPerRow * BitmapHeight;

  /// <summary>Colours a Graphics 7 screen can show at once.</summary>
  public const int ColorCount = 4;

  /// <summary>Total file size — the bitmap and nothing else.</summary>
  public const int FileSize = BitmapDataSize;

  /// <summary>The XL/XE operating system's default playfield colours, which Mamut relies on.</summary>
  public static ReadOnlySpan<byte> DefaultColors => [0x00, 0x28, 0xCA, 0x94];

  static string IImageFormatMetadata<MamutFile>.PrimaryExtension => ".rys";
  static string[] IImageFormatMetadata<MamutFile>.FileExtensions => [".rys"];
  static MamutFile IImageFormatReader<MamutFile>.FromSpan(ReadOnlySpan<byte> data)
    => MamutReader.FromSpan(data);
  static byte[] IImageFormatWriter<MamutFile>.ToBytes(MamutFile file)
    => MamutWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MamutFile>.VideoModes => [
    new("Graphics 7", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Packed Graphics 7 bitmap.</summary>
  public byte[] BitmapData { get; init; }


  public static RawImage ToRawImage(MamutFile file) {
    var pixels = Atari8BitGraphics.UnpackGr7(file.BitmapData, 0, BitmapHeight);
    var gtia = Atari8BitGraphics.CreatePalette();

    // No stored palette: fall back to the OS defaults the format assumes.
    var palette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value)
      Array.Copy(gtia, DefaultColors[value] * 3, palette, value * 3, 3);

    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[(y >> 1) * BitmapWidth + (x >> 1)];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static MamutFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // The palette is fixed by the operating system, so map onto it rather than choosing colours.
    var gtia = Atari8BitGraphics.CreatePalette();
    var fixedPalette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value)
      Array.Copy(gtia, DefaultColors[value] * 3, fixedPalette, value * 3, 3);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, fixedPalette);

    var pixels = new byte[BitmapWidth * BitmapHeight];
    for (var y = 0; y < BitmapHeight; ++y)
    for (var x = 0; x < BitmapWidth; ++x) {
      var index = indexed.PixelData[y * 2 * DisplayWidth + x * 2];
      pixels[y * BitmapWidth + x] = (byte)(index < ColorCount ? index : 0);
    }

    return new() { BitmapData = Atari8BitGraphics.PackGr7(pixels, BitmapHeight) };
  }
}
