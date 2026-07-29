using System;
using FileFormat.Core;

namespace FileFormat.AtariTt;

/// <summary>In-memory representation of an Atari TT screen (.pi4, .pi5, .pi6).</summary>
/// <remarks>
/// The TT extends the DEGAS layout to the three resolutions the machine adds: a mode byte, a
/// palette, and then an uncompressed word-interleaved bitplane bitmap. Every one of them stores
/// exactly 153600 bytes of bitmap, so the mode byte and the palette size are what tell them apart.
/// TT colours carry four bits per channel where the ST had three.
/// </remarks>
public readonly record struct AtariTtFile
  : IImageFormatReader<AtariTtFile>, IImageToRawImage<AtariTtFile>,
    IImageFromRawImage<AtariTtFile>, IImageFormatWriter<AtariTtFile> {

  /// <summary>Bytes of bitmap every TT resolution stores.</summary>
  public const int BitmapDataSize = 153600;

  /// <summary>Offset of the palette; the two bytes before it are the file and mode identifiers.</summary>
  public const int PaletteOffset = 2;

  static string IImageFormatMetadata<AtariTtFile>.PrimaryExtension => ".pi5";
  static string[] IImageFormatMetadata<AtariTtFile>.FileExtensions => [".pi5", ".pi4", ".pi6"];
  static AtariTtFile IImageFormatReader<AtariTtFile>.FromSpan(ReadOnlySpan<byte> data) => AtariTtReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariTtFile>.ToBytes(AtariTtFile file) => AtariTtWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariTtFile>.VideoModes => [
    new("TT Medium (640x480, 16 colours)", [(640, 480)], [new IntegerRange(2, 16)]),
    new("TT Low (320x480 shown across 640, 256 colours)", [(640, 480)], [new IntegerRange(17, 256)]),
    new("TT High (1280x960, monochrome)", [(1280, 960)], [2]),
  ];

  /// <summary>Which TT resolution the screen is in.</summary>
  public AtariTtResolution Resolution { get; init; }

  /// <summary>The palette, one packed 16-bit TT colour per entry; empty in the monochrome mode.</summary>
  public short[] Palette { get; init; }

  /// <summary>The uncompressed word-interleaved bitplane bitmap.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Bitplanes a resolution uses.</summary>
  public static int BitplanesFor(AtariTtResolution resolution) => resolution switch {
    AtariTtResolution.Low => 8,
    AtariTtResolution.Medium => 4,
    AtariTtResolution.High => 1,
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown TT resolution."),
  };

  /// <summary>Palette entries a resolution stores.</summary>
  public static int PaletteCountFor(AtariTtResolution resolution) => resolution switch {
    AtariTtResolution.Low => 256,
    AtariTtResolution.Medium => 16,
    AtariTtResolution.High => 2,
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown TT resolution."),
  };

  /// <summary>Pixels stored per row, before the display doubles them.</summary>
  public static int StoredWidthFor(AtariTtResolution resolution) => resolution switch {
    AtariTtResolution.Low => 320,
    AtariTtResolution.Medium => 640,
    AtariTtResolution.High => 1280,
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown TT resolution."),
  };

  /// <summary>Rows stored.</summary>
  public static int StoredHeightFor(AtariTtResolution resolution)
    => resolution == AtariTtResolution.High ? 960 : 480;

  /// <summary>Displayed size; TT Low stretches its 320 stored pixels across 640.</summary>
  public static (int Width, int Height) DisplaySizeFor(AtariTtResolution resolution)
    => resolution == AtariTtResolution.Low ? (640, 480) : (StoredWidthFor(resolution), StoredHeightFor(resolution));

  /// <summary>Offset of the bitmap; it follows the palette, which the mode sizes.</summary>
  public static int BitmapOffsetFor(AtariTtResolution resolution)
    => PaletteOffset + PaletteCountFor(resolution) * 2;

  /// <summary>Total file size.</summary>
  public static int FileSizeFor(AtariTtResolution resolution) => BitmapOffsetFor(resolution) + BitmapDataSize;

  /// <summary>Expands a packed TT colour into an RGB triplet.</summary>
  /// <remarks>Four bits per channel, each doubled up into a byte so that 15 becomes 255.</remarks>
  internal static void UnpackColor(short packed, Span<byte> rgb) {
    var value = (ushort)packed;
    rgb[0] = (byte)(((value >> 8) & 15) * 17);
    rgb[1] = (byte)(((value >> 4) & 15) * 17);
    rgb[2] = (byte)((value & 15) * 17);
  }

  /// <summary>Packs an RGB triplet into a TT colour.</summary>
  internal static short PackColor(byte red, byte green, byte blue)
    => (short)(((red * 15 / 255) << 8) | ((green * 15 / 255) << 4) | (blue * 15 / 255));

  public static RawImage ToRawImage(AtariTtFile file) {
    var resolution = file.Resolution;
    var storedWidth = StoredWidthFor(resolution);
    var storedHeight = StoredHeightFor(resolution);
    var chunky = PlanarConverter.AtariStToChunky(file.BitmapData, storedWidth, storedHeight, BitplanesFor(resolution));

    var (width, height) = DisplaySizeFor(resolution);
    var pixels = chunky;
    if (width != storedWidth) {
      // TT Low is shown two screen pixels wide, so widen it here rather than leaving callers to
      // guess the aspect.
      pixels = new byte[width * height];
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = chunky[y * storedWidth + (x >> 1)];
    }

    var count = PaletteCountFor(resolution);
    var palette = new byte[count * 3];
    if (resolution == AtariTtResolution.High) {
      // The monochrome mode has no stored palette: a set bit is ink on white paper.
      palette[0] = palette[1] = palette[2] = 255;
    } else {
      var stored = file.Palette ?? [];
      for (var i = 0; i < count; ++i)
        UnpackColor(i < stored.Length ? stored[i] : (short)0, palette.AsSpan(i * 3, 3));
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = count,
    };
  }

  public static AtariTtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if ((image.Width, image.Height) is not ((1280, 960) or (640, 480)))
      throw new ArgumentException($"An Atari TT screen is 640x480 or 1280x960, got {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureFormat(PixelFormat.Indexed8);

    // At 640x480 the machine offers a choice: sixteen colours at full width, or 256 at half. Take
    // the wider one unless the picture needs more than sixteen colours.
    var resolution = image.Height == 960 ? AtariTtResolution.High
      : indexed.PaletteCount > 16 ? AtariTtResolution.Low
      : AtariTtResolution.Medium;

    var storedWidth = StoredWidthFor(resolution);
    var storedHeight = StoredHeightFor(resolution);
    var count = PaletteCountFor(resolution);

    var sourcePalette = indexed.Palette ?? [];
    var palette = new short[count];
    for (var i = 0; i < count && i < indexed.PaletteCount; ++i)
      palette[i] = PackColor(sourcePalette[i * 3], sourcePalette[i * 3 + 1], sourcePalette[i * 3 + 2]);

    var chunky = indexed.PixelData;
    if (resolution == AtariTtResolution.High) {
      // The monochrome mode's set bits are ink, so only the low bit of each index survives.
      chunky = new byte[storedWidth * storedHeight];
      for (var i = 0; i < chunky.Length && i < indexed.PixelData.Length; ++i)
        chunky[i] = (byte)(indexed.PixelData[i] & 1);
    } else if (storedWidth != image.Width) {
      // TT Low stores half as many pixels as it shows, so sample every other column.
      chunky = new byte[storedWidth * storedHeight];
      for (var y = 0; y < storedHeight; ++y)
      for (var x = 0; x < storedWidth; ++x)
        chunky[y * storedWidth + x] = indexed.PixelData[y * image.Width + x * 2];
    }

    return new() {
      Resolution = resolution,
      Palette = palette,
      BitmapData = PlanarConverter.ChunkyToAtariSt(chunky, storedWidth, storedHeight, BitplanesFor(resolution)),
    };
  }
}
