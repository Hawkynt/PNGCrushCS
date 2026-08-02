using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeScreen;

/// <summary>In-memory representation of a SAM Coupe mode 1, 2 or 3 screen (.ss1, .ss2, .ss3).</summary>
/// <remarks>
/// The three screens the SAM Coupe inherited or invented on its way to mode 4. Mode 1 is the ZX
/// Spectrum's display exactly — the same shuffled line order, the same one attribute per 8x8 cell —
/// which is what made the machine able to run Spectrum software. Mode 2 keeps the one-bit bitmap
/// but gives every scanline its own attribute byte, so a cell is no longer stuck with one pair of
/// colours. Mode 3 drops attributes altogether for two bits per pixel across 512, which costs half
/// the vertical resolution and is why its rows are drawn twice.
/// <para/>
/// All three end with a palette, a list of line interrupts and a 0xFF terminator. An interrupt
/// rewrites one palette entry part-way down the screen, so a single screen can show more than
/// sixteen colours — and so the decoded picture cannot be expressed as one indexed image with one
/// palette. That is why these decode to RGB.
/// </remarks>
public readonly record struct SamCoupeScreenFile
  : IImageFormatReader<SamCoupeScreenFile>, IImageToRawImage<SamCoupeScreenFile> {

  /// <summary>Scanlines the hardware displays.</summary>
  public const int ScreenHeight = 192;

  /// <summary>Colours the palette holds.</summary>
  public const int PaletteSize = 16;

  /// <summary>Bytes between the palette and the interrupt list.</summary>
  public const int PaletteToInterruptGap = 40;

  /// <summary>Bytes in one interrupt record: line, entry, colour, and one the hardware ignores.</summary>
  public const int InterruptRecordSize = 4;

  /// <summary>Byte that closes the interrupt list.</summary>
  public const byte InterruptTerminator = 0xFF;

  /// <summary>Offset of the interrupt list, which also fixes where the palette sits.</summary>
  public static int InterruptOffsetFor(SamCoupeScreenMode mode) => mode switch {
    SamCoupeScreenMode.Mode1 => 6952,
    SamCoupeScreenMode.Mode2 => 14376,
    _ => 24616,
  };

  /// <summary>Offset of the palette.</summary>
  public static int PaletteOffsetFor(SamCoupeScreenMode mode) => InterruptOffsetFor(mode) - PaletteToInterruptGap;

  /// <summary>Stored pixels per row.</summary>
  public static int WidthFor(SamCoupeScreenMode mode) => mode == SamCoupeScreenMode.Mode3 ? 512 : 256;

  /// <summary>Scanlines one stored row is drawn on.</summary>
  public static int RowScaleFor(SamCoupeScreenMode mode) => mode == SamCoupeScreenMode.Mode3 ? 2 : 1;

  /// <summary>Displayed height.</summary>
  public static int DisplayHeightFor(SamCoupeScreenMode mode) => ScreenHeight * RowScaleFor(mode);

  /// <summary>Which screen an extension names.</summary>
  public static SamCoupeScreenMode ModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".ss1" => SamCoupeScreenMode.Mode1,
    ".ss2" => SamCoupeScreenMode.Mode2,
    _ => SamCoupeScreenMode.Mode3,
  };

  static string IImageFormatMetadata<SamCoupeScreenFile>.PrimaryExtension => ".ss1";
  static string[] IImageFormatMetadata<SamCoupeScreenFile>.FileExtensions => [".ss1", ".ss2", ".ss3"];
  static SamCoupeScreenFile IImageFormatReader<SamCoupeScreenFile>.FromSpan(ReadOnlySpan<byte> data)
    => SamCoupeScreenReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static SamCoupeScreenFile IImageFormatReader<SamCoupeScreenFile>.FromFile(FileInfo file) => SamCoupeScreenReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<SamCoupeScreenFile>.VideoModes => [
    new("Mode 1", [(256, ScreenHeight)], [PaletteSize]),
    new("Mode 2", [(256, ScreenHeight)], [PaletteSize]),
    new("Mode 3", [(512, ScreenHeight * 2)], [4]),
  ];

  /// <summary>Which screen this is.</summary>
  public SamCoupeScreenMode Mode { get; init; }

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>Displayed width.</summary>
  public int Width => WidthFor(this.Mode);

  /// <summary>Displayed height.</summary>
  public int Height => DisplayHeightFor(this.Mode);

  public static RawImage ToRawImage(SamCoupeScreenFile file) {
    var data = file.Data ?? [];
    var mode = file.Mode;
    var width = WidthFor(mode);
    var scale = RowScaleFor(mode);
    var rgb = new byte[width * ScreenHeight * scale * 3];

    var palette = new int[PaletteSize];
    var paletteOffset = PaletteOffsetFor(mode);
    for (var i = 0; i < PaletteSize; ++i)
      palette[i] = SamCoupePalette.ToRgb(_At(data, paletteOffset + i));

    var interrupt = InterruptOffsetFor(mode);
    for (var y = 0; y < ScreenHeight; ++y) {
      // Records are in line order and each names the line *before* the one it takes effect on, so
      // several can land on the same scanline and the list is walked, not indexed.
      while (interrupt + InterruptRecordSize - 1 < data.Length && y == data[interrupt] + 1) {
        var entry = data[interrupt + 1];
        if (entry >= PaletteSize)
          break;

        palette[entry] = SamCoupePalette.ToRgb(data[interrupt + 2]);
        interrupt += InterruptRecordSize;
      }

      for (var x = 0; x < width; ++x) {
        var color = palette[_ColorIndex(data, mode, x, y)];
        for (var repeat = 0; repeat < scale; ++repeat) {
          var target = ((y * scale + repeat) * width + x) * 3;
          rgb[target] = (byte)(color >> 16);
          rgb[target + 1] = (byte)(color >> 8);
          rgb[target + 2] = (byte)color;
        }
      }
    }

    return new() { Width = width, Height = ScreenHeight * scale, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>The palette entry a pixel draws from.</summary>
  private static int _ColorIndex(ReadOnlySpan<byte> data, SamCoupeScreenMode mode, int x, int y) {
    if (mode == SamCoupeScreenMode.Mode3) {
      // Two bits per pixel, and the pair is stored the other way round from every other mode.
      var pair = _At(data, (y << 7) | (x >> 2)) >> ((~x & 3) << 1);
      return ((pair & 1) << 1) | ((pair >> 1) & 1);
    }

    var bitmap = mode == SamCoupeScreenMode.Mode1 ? ZxSpectrumGraphics.LineOffset(y) : y << 5;
    var attributes = mode == SamCoupeScreenMode.Mode1 ? 6144 + ((y >> 3) << 5) : 8192 + (y << 5);
    var attribute = _At(data, attributes + (x >> 3));
    var set = ((_At(data, bitmap + (x >> 3)) >> (~x & 7)) & 1) != 0;

    // Bit 6 of the attribute is the bright flag and becomes the palette entry's high bit; ink is
    // the low three bits, paper the next three.
    return ((attribute >> 3) & 8) | ((set ? attribute : attribute >> 3) & 7);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset) => offset < data.Length ? data[offset] : (byte)0;
}
