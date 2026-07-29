using System;
using FileFormat.Core;

namespace FileFormat.MagicPainter;

/// <summary>In-memory representation of a Magic Painter (.mgp) Atari 8-bit picture.</summary>
/// <remarks>
/// A fixed 3845-byte file: the five GTIA colour registers PF0-PF3 and BAK, a "rainbow" byte that
/// selects the program's colour-cycling effect, then the ANTIC mode D ("Graphics 7") bitmap. The
/// bitmap section is one byte shorter than a full screen; readers treat the missing trailing byte
/// as zero. The 160x96 logical pixels are displayed at 320x192.
/// </remarks>
public readonly record struct MagicPainterFile : IImageFormatReader<MagicPainterFile>, IImageToRawImage<MagicPainterFile>, IImageFromRawImage<MagicPainterFile>, IImageFormatWriter<MagicPainterFile> {

  /// <summary>Logical bitmap width.</summary>
  public const int BitmapWidth = Atari8BitGraphics.Gr7Width;

  /// <summary>Number of stored scanlines.</summary>
  public const int BitmapHeight = 96;

  /// <summary>Displayed width; each logical pixel is two screen pixels wide.</summary>
  public const int DisplayWidth = BitmapWidth * 2;

  /// <summary>Displayed height; each stored scanline is shown twice.</summary>
  public const int DisplayHeight = BitmapHeight * 2;

  /// <summary>Size of a full Graphics 7 screen.</summary>
  public const int BitmapDataSize = Atari8BitGraphics.Gr7BytesPerRow * BitmapHeight;

  /// <summary>Offset of the "rainbow" effect byte.</summary>
  public const int RainbowOffset = Atari8BitGraphics.ColorRegisterCount;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = RainbowOffset + 1;

  /// <summary>Bitmap bytes actually stored — the final byte of the screen is omitted.</summary>
  public const int StoredBitmapSize = BitmapDataSize - 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = BitmapOffset + StoredBitmapSize;

  /// <summary>Colours a Graphics 7 screen can show at once.</summary>
  public const int ColorCount = 4;

  static string IImageFormatMetadata<MagicPainterFile>.PrimaryExtension => ".mgp";
  static string[] IImageFormatMetadata<MagicPainterFile>.FileExtensions => [".mgp"];
  static MagicPainterFile IImageFormatReader<MagicPainterFile>.FromSpan(ReadOnlySpan<byte> data) => MagicPainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<MagicPainterFile>.ToBytes(MagicPainterFile file) => MagicPainterWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MagicPainterFile>.VideoModes => [
    new("Graphics 7", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Packed Graphics 7 bitmap, padded to a full screen.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The five GTIA colour registers: PF0, PF1, PF2, PF3, BAK.</summary>
  public byte[] ColorRegisters { get; init; }

  /// <summary>Colour-cycling effect selector; preserved but not applied.</summary>
  public byte Rainbow { get; init; }

  public static RawImage ToRawImage(MagicPainterFile file) {
    var pixels = Atari8BitGraphics.UnpackGr7(file.BitmapData, 0, BitmapHeight);
    var gtia = Atari8BitGraphics.CreatePalette();

    var palette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value) {
      var register = Atari8BitGraphics.RegisterForPixel(value);
      var colorByte = register < file.ColorRegisters.Length ? file.ColorRegisters[register] : (byte)0;
      Array.Copy(gtia, colorByte * 3, palette, value * 3, 3);
    }

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

  public static MagicPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed4);
    var palette = indexed.Palette ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var registers = new byte[Atari8BitGraphics.ColorRegisterCount];
    for (var value = 0; value < ColorCount && value < indexed.PaletteCount; ++value) {
      var register = Atari8BitGraphics.RegisterForPixel(value);
      registers[register] = Atari8BitGraphics.FindNearestColorByte(
        gtia, palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);
    }

    var pixels = new byte[BitmapWidth * BitmapHeight];
    for (var y = 0; y < BitmapHeight; ++y)
    for (var x = 0; x < BitmapWidth; ++x) {
      var source = y * 2 * DisplayWidth + x * 2;
      var b = indexed.PixelData[source >> 1];
      var index = (source & 1) == 0 ? (b >> 4) & 0x0F : b & 0x0F;
      pixels[y * BitmapWidth + x] = (byte)(index < ColorCount ? index : 0);
    }

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, BitmapHeight),
      ColorRegisters = registers,
      Rainbow = 0,
    };
  }
}
