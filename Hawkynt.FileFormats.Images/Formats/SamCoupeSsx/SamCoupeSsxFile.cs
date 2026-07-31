using System;
using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeSsx;

/// <summary>In-memory representation of a SAM Coupe screen dump (.ssx).</summary>
/// <remarks>
/// A dump of whichever of the machine's four screen modes was running, with the palette appended.
/// Nothing in the file says which mode; the length does, because no two of them occupy the same
/// number of bytes. The SAM was designed as a Spectrum successor and its first two modes show it —
/// mode 1 is the Spectrum's screen exactly, thirds and all, and mode 2 keeps the layout but gives
/// every scanline its own attributes instead of every eighth. Modes 3 and 4 drop the attribute
/// scheme entirely for straight bitmaps.
/// <para/>
/// One further form is not a screen at all but a byte per pixel across the full 512, which is what
/// a program produced when it rendered rather than displayed.
/// </remarks>
public readonly record struct SamCoupeSsxFile
  : IImageFormatReader<SamCoupeSsxFile>, IImageToRawImage<SamCoupeSsxFile> {

  /// <summary>Rows every form stores.</summary>
  public const int StoredRows = 192;

  /// <summary>Size of a mode 1 dump: a Spectrum screen and sixteen colours.</summary>
  public const int Mode1Size = 6928;

  /// <summary>Size of a mode 2 dump: attributes per scanline rather than per character row.</summary>
  public const int Mode2Size = 12304;

  /// <summary>Size of a mode 3 dump: four colours across 512 pixels.</summary>
  public const int Mode3Size = 24580;

  /// <summary>Size of a mode 4 dump: sixteen colours across 256 pixels.</summary>
  public const int Mode4Size = 24592;

  /// <summary>Size of a rendered dump: one of 128 colours per pixel across 512.</summary>
  public const int ChunkySize = 98304;

  static string IImageFormatMetadata<SamCoupeSsxFile>.PrimaryExtension => ".ssx";
  static string[] IImageFormatMetadata<SamCoupeSsxFile>.FileExtensions => [".ssx"];
  static SamCoupeSsxFile IImageFormatReader<SamCoupeSsxFile>.FromSpan(ReadOnlySpan<byte> data)
    => SamCoupeSsxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SamCoupeSsxFile>.VideoModes => [
    new("Modes 1, 2 and 4", [(256, StoredRows)], [16]),
    new("Mode 3", [(512, StoredRows * 2)], [4]),
    new("Rendered", [(512, StoredRows * 2)], [128]),
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SamCoupeSsxFile file) {
    var data = file.Data ?? [];

    return data.Length switch {
      Mode1Size => _Attributed(data, 6912, false),
      Mode2Size => _Attributed(data, 12288, true),
      Mode3Size => _Doubled(data, SamCoupePalette.ToRgbTriplets(data.AsSpan(24576, 4)), 4,
        (y, x) => (data[(y << 7) | (x >> 2)] >> ((~x & 3) << 1)) & 3),
      Mode4Size => _Nibbles(data),
      _ => _Doubled(data, _FullPalette(), 128, (y, x) => data[(y << 9) + x]),
    };
  }

  /// <summary>All 128 colours the hardware can make, for a dump that names them directly.</summary>
  private static byte[] _FullPalette() {
    var values = new byte[128];
    for (var i = 0; i < values.Length; ++i)
      values[i] = (byte)i;

    return SamCoupePalette.ToRgbTriplets(values);
  }

  /// <summary>Renders one of the two Spectrum-derived modes, which differ only in the attributes.</summary>
  /// <param name="perScanline">
  /// Whether every scanline carries its own attributes rather than every character row. That is
  /// mode 2's whole contribution: the same 256x192 screen, freed of the constraint that made the
  /// Spectrum's colour clash.
  /// </param>
  private static RawImage _Attributed(ReadOnlySpan<byte> data, int paletteOffset, bool perScanline) {
    const int width = 256;
    var palette = _AttributePalette(data, paletteOffset);
    var pixels = new byte[width * StoredRows];

    for (var y = 0; y < StoredRows; ++y)
    for (var x = 0; x < width; ++x) {
      var column = x >> 3;

      // Mode 1 keeps the Spectrum's scrambled display file; mode 2 lays its rows out in order.
      var bitmap = perScanline ? (y << 5) | column : ZxSpectrumGraphics.LineOffset(y) + column;
      var ink = ((data[bitmap] >> (~x & 7)) & 1) != 0;

      var attribute = data[6144 + (perScanline ? y << 5 : (y >> 3) << 5) + column];
      pixels[y * width + x] = (byte)ZxSpectrumGraphics.ColorIndex(attribute, ink);
    }

    return new() {
      Width = width,
      Height = StoredRows,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ZxSpectrumGraphics.PaletteEntryCount,
    };
  }

  /// <summary>
  /// Expands the sixteen stored colours into the palette an attribute byte addresses.
  /// </summary>
  /// <remarks>
  /// The SAM keeps the Spectrum's attribute byte but replaces the fixed colours behind it, so what
  /// is stored is eight inks and eight bright inks — and the bright bit, not the paper bit, is what
  /// chooses between the halves.
  /// </remarks>
  private static byte[] _AttributePalette(ReadOnlySpan<byte> data, int offset) {
    var values = new byte[ZxSpectrumGraphics.PaletteEntryCount];
    for (var i = 0; i < values.Length; ++i)
      values[i] = data[offset + i];

    return SamCoupePalette.ToRgbTriplets(values);
  }

  private static RawImage _Nibbles(ReadOnlySpan<byte> data) {
    const int width = 256;
    var pixels = new byte[width * StoredRows];
    for (var y = 0; y < StoredRows; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)MsxGraphics.GetNibble(data, y * 128, x);

    return new() {
      Width = width,
      Height = StoredRows,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = SamCoupePalette.ToRgbTriplets(data.Slice(24576, 16)),
      PaletteCount = 16,
    };
  }

  /// <summary>Renders a 512-pixel mode, whose rows are each shown twice.</summary>
  private static RawImage _Doubled(ReadOnlySpan<byte> data, byte[] palette, int colors, Func<int, int, int> index) {
    const int width = 512;
    var pixels = new byte[width * StoredRows * 2];

    for (var y = 0; y < StoredRows; ++y)
    for (var x = 0; x < width; ++x)
      pixels[(y * 2 + 1) * width + x] = pixels[y * 2 * width + x] = (byte)index(y, x);

    return new() {
      Width = width,
      Height = StoredRows * 2,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = colors,
    };
  }
}
