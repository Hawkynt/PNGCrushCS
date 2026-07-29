using System;
using FileFormat.Core;

namespace FileFormat.AtariGraphics3;

/// <summary>In-memory representation of an Atari 8-bit Graphics 3 screen.</summary>
/// <remarks>
/// ANTIC mode 8 is the coarsest bitmap the hardware offers — 40x24 pixels drawn as 8x8 blocks —
/// so a whole screen is 240 bytes. Two formats share it: Standard Graphics 3 (.sg3) stores the
/// screen alone and takes the operating system's default colours, while Mad Studio's variant
/// (.gr3) appends four GTIA colour bytes.
/// </remarks>
public readonly record struct AtariGraphics3File
  : IImageFormatReader<AtariGraphics3File>, IImageToRawImage<AtariGraphics3File>,
    IImageFromRawImage<AtariGraphics3File>, IImageFormatWriter<AtariGraphics3File> {

  /// <summary>Size of the screen data.</summary>
  public const int ScreenDataSize = Atari8BitGraphics.Gr3DataSize;

  /// <summary>File size without stored colours.</summary>
  public const int PlainFileSize = ScreenDataSize;

  /// <summary>File size with the four colour bytes appended.</summary>
  public const int ColoredFileSize = ScreenDataSize + ColorCount;

  /// <summary>Colours a Graphics 3 screen can show.</summary>
  public const int ColorCount = 4;

  /// <summary>Displayed width; each logical pixel is an 8x8 block.</summary>
  public const int DisplayWidth = Atari8BitGraphics.Gr3Width * 8;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = Atari8BitGraphics.Gr3Height * 8;

  /// <summary>The XL/XE operating system's default playfield colours.</summary>
  public static ReadOnlySpan<byte> DefaultColors => [0x00, 0x28, 0xCA, 0x94];

  static string IImageFormatMetadata<AtariGraphics3File>.PrimaryExtension => ".gr3";
  static string[] IImageFormatMetadata<AtariGraphics3File>.FileExtensions => [".gr3", ".sg3"];
  static AtariGraphics3File IImageFormatReader<AtariGraphics3File>.FromSpan(ReadOnlySpan<byte> data)
    => AtariGraphics3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariGraphics3File>.ToBytes(AtariGraphics3File file)
    => AtariGraphics3Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariGraphics3File>.VideoModes => [
    new("Graphics 3", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Packed mode 8 screen.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>The four GTIA colour bytes, background first.</summary>
  public byte[] Colors { get; init; }

  /// <summary>Whether the colours were stored in the file rather than taken from the defaults.</summary>
  public bool HasStoredColors { get; init; }

  public static RawImage ToRawImage(AtariGraphics3File file) {
    var pixels = Atari8BitGraphics.UnpackGr3(file.ScreenData, 0);
    var gtia = Atari8BitGraphics.CreatePalette();
    var colors = file.HasStoredColors ? file.Colors : DefaultColors.ToArray();

    var palette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value) {
      var colorByte = value < colors.Length ? colors[value] : (byte)0;
      Array.Copy(gtia, colorByte * 3, palette, value * 3, 3);
    }

    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[(y >> 3) * Atari8BitGraphics.Gr3Width + (x >> 3)];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static AtariGraphics3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed4);
    var palette = indexed.Palette ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var colors = new byte[ColorCount];
    for (var value = 0; value < ColorCount && value < indexed.PaletteCount; ++value)
      colors[value] = Atari8BitGraphics.FindNearestColorByte(
        gtia, palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);

    var pixels = new byte[Atari8BitGraphics.Gr3Width * Atari8BitGraphics.Gr3Height];
    for (var y = 0; y < Atari8BitGraphics.Gr3Height; ++y)
    for (var x = 0; x < Atari8BitGraphics.Gr3Width; ++x) {
      var source = (y * 8) * DisplayWidth + x * 8;
      var b = indexed.PixelData[source >> 1];
      var index = (source & 1) == 0 ? (b >> 4) & 0x0F : b & 0x0F;
      pixels[y * Atari8BitGraphics.Gr3Width + x] = (byte)(index < ColorCount ? index : 0);
    }

    return new() {
      ScreenData = Atari8BitGraphics.PackGr3(pixels),
      Colors = colors,
      HasStoredColors = true,
    };
  }
}
