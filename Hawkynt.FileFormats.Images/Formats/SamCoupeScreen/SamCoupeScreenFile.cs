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
  : IImageFormatReader<SamCoupeScreenFile>, IImageToRawImage<SamCoupeScreenFile>,
    IImageFromRawImage<SamCoupeScreenFile>, IImageFormatWriter<SamCoupeScreenFile> {

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
  static byte[] IImageFormatWriter<SamCoupeScreenFile>.ToBytes(SamCoupeScreenFile file)
    => SamCoupeScreenWriter.ToBytes(file);

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

  /// <summary>Offset of a mode 1 screen's attributes, one per 8x8 cell.</summary>
  public const int Mode1AttributeOffset = 6144;

  /// <summary>Bytes a mode 1 screen occupies once its empty interrupt list is closed.</summary>
  public const int Mode1FileSize = 6953;

  /// <summary>
  /// Builds a screen from any image, sampling it to the 256x192 of mode 1.
  /// </summary>
  /// <remarks>
  /// Mode 1 of the three, because the extension is what names the mode and this type's is .ss1. It
  /// is the Spectrum's display exactly — the same shuffled line order, the same one attribute per
  /// 8x8 cell — with the SAM's own sixteen colours behind the attribute rather than the Spectrum's
  /// fixed ones. A cell names an ink and a paper of three bits each and one bright flag they share,
  /// so its two colours must both come from the same half of the palette; every way of choosing them
  /// is tried against the cell's 64 pixels, which is exact for a picture the screen can hold.
  /// <para/>
  /// Which half a colour lands in therefore matters, and the palette is ordered by brightness: the
  /// darker eight of the sixteen fill the low half and the brighter eight the high one. That is what
  /// the flag meant on the machine this display came from, and it is the ordering under which a cell
  /// wanting two dark colours or two light ones — which is most of them — can have both.
  /// <para/>
  /// No line interrupts are written. One rewrites a palette entry part-way down the screen, and
  /// which entry a picture can most afford to have changed, and where, is not something a picture
  /// says.
  /// </remarks>
  public static SamCoupeScreenFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int width = 256;
    var rgb = image.SampleTo(width, ScreenHeight).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var data = new byte[Mode1FileSize];
    data[InterruptOffsetFor(SamCoupeScreenMode.Mode1)] = InterruptTerminator;

    var stored = data.AsSpan(PaletteOffsetFor(SamCoupeScreenMode.Mode1), PaletteSize);
    var reduced = new RawImage {
      Width = width, Height = ScreenHeight, Format = PixelFormat.Rgb24, PixelData = rgb,
    }.EnsureIndexedAtMost(PaletteSize);
    _StorePalette(reduced.Palette ?? [], stored);

    var palette = new int[PaletteSize];
    for (var i = 0; i < PaletteSize; ++i)
      palette[i] = SamCoupePalette.ToRgb(stored[i]);

    for (var top = 0; top < ScreenHeight; top += 8)
    for (var left = 0; left < width; left += 8) {
      var (bright, ink, paper) = _ChooseAttribute(rgb, width, palette, left, top);
      data[Mode1AttributeOffset + ((top >> 3) << 5) + (left >> 3)]
        = (byte)((bright << 6) | (paper << 3) | ink);

      var lit = palette[(bright << 3) | ink];
      var unlit = palette[(bright << 3) | paper];

      for (var y = 0; y < 8; ++y) {
        var bits = 0;
        for (var x = 0; x < 8; ++x) {
          var at = ((top + y) * width + left + x) * 3;
          if (_Distance(rgb, at, lit) <= _Distance(rgb, at, unlit))
            bits |= 1 << (~x & 7);
        }

        // Mode 1 keeps the Spectrum's scrambled display file, thirds and all.
        data[ZxSpectrumGraphics.LineOffset(top + y) + (left >> 3)] = (byte)bits;
      }
    }

    return new() { Mode = SamCoupeScreenMode.Mode1, Data = data };
  }

  /// <summary>
  /// Snaps the chosen colours to what the hardware can make and stores them darkest first.
  /// </summary>
  /// <remarks>
  /// The order is the whole of what makes the bright flag usable: a cell's ink and paper share it,
  /// so the two must be in the same half, and putting the darker eight in one half and the brighter
  /// eight in the other is what lets a cell that wants two of either have them.
  /// </remarks>
  private static void _StorePalette(ReadOnlySpan<byte> palette, Span<byte> stored) {
    var colors = new (int Brightness, byte Value)[PaletteSize];

    for (var i = 0; i < PaletteSize; ++i) {
      var entry = i * 3;
      var value = entry + 2 < palette.Length
        ? SamCoupePalette.FromRgb(palette[entry], palette[entry + 1], palette[entry + 2])
        : (byte)0;

      var rgb = SamCoupePalette.ToRgb(value);
      colors[i] = (((rgb >> 16) & 0xFF) + ((rgb >> 8) & 0xFF) + (rgb & 0xFF), value);
    }

    // The value breaks ties, so two colours of equal brightness still land in a settled order.
    Array.Sort(colors, (left, right) => left.Brightness != right.Brightness
      ? left.Brightness.CompareTo(right.Brightness)
      : left.Value.CompareTo(right.Value));

    for (var i = 0; i < PaletteSize; ++i)
      stored[i] = colors[i].Value;
  }

  /// <summary>The ink, paper and shared bright flag that describe a cell with the least error.</summary>
  private static (int Bright, int Ink, int Paper) _ChooseAttribute(
    ReadOnlySpan<byte> rgb, int width, ReadOnlySpan<int> palette, int left, int top) {
    int bestBright = 0, bestInk = 0, bestPaper = 0;
    var bestError = long.MaxValue;

    for (var bright = 0; bright < 2; ++bright)
    for (var ink = 0; ink < 8; ++ink)
    for (var paper = 0; paper <= ink; ++paper) {
      var lit = palette[(bright << 3) | ink];
      var unlit = palette[(bright << 3) | paper];

      long error = 0;
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = ((top + y) * width + left + x) * 3;
        error += Math.Min(_Distance(rgb, at, lit), _Distance(rgb, at, unlit));
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestBright = bright;
      bestInk = ink;
      bestPaper = paper;
    }

    return (bestBright, bestInk, bestPaper);
  }

  /// <summary>Squared distance between a pixel and a packed colour.</summary>
  private static int _Distance(ReadOnlySpan<byte> rgb, int at, int color) {
    int dr = rgb[at] - ((color >> 16) & 0xFF);
    int dg = rgb[at + 1] - ((color >> 8) & 0xFF);
    int db = rgb[at + 2] - (color & 0xFF);

    return dr * dr + dg * dg + db * db;
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
