using System;
using FileFormat.Core;

namespace FileFormat.CentauriLogoEditor;

/// <summary>In-memory representation of a Centauri Logo-Editor picture (.cle) for the Commodore 64.</summary>
/// <remarks>
/// A full screen in four freely chosen colours, with no per-cell attributes: two bits a pixel
/// against one palette for the whole picture. That is fewer colours than a multicolour screen but
/// no restriction on where they go, which for a logo is the better trade.
/// <para/>
/// The four registers are not stored in order. Two of them share one byte's nibbles and the other
/// two have a byte each, which is how the C64's registers happen to sit in memory rather than
/// anything the format chose.
/// </remarks>
public readonly record struct CentauriLogoEditorFile
  : IImageFormatReader<CentauriLogoEditorFile>, IImageToRawImage<CentauriLogoEditorFile>,
    IImageFromRawImage<CentauriLogoEditorFile>, IImageFormatWriter<CentauriLogoEditorFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 200;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Offset of the byte holding the second and third colours.</summary>
  public const int PairOffset = 8002;

  /// <summary>Offset of the byte holding the fourth colour.</summary>
  public const int FourthOffset = 8003;

  /// <summary>Offset of the byte holding the first colour.</summary>
  public const int FirstOffset = 8004;

  /// <summary>Total file size.</summary>
  public const int FileSize = 8194;

  static string IImageFormatMetadata<CentauriLogoEditorFile>.PrimaryExtension => ".cle";
  static string[] IImageFormatMetadata<CentauriLogoEditorFile>.FileExtensions => [".cle"];
  static CentauriLogoEditorFile IImageFormatReader<CentauriLogoEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => CentauriLogoEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<CentauriLogoEditorFile>.ToBytes(CentauriLogoEditorFile file)
    => CentauriLogoEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CentauriLogoEditorFile>.VideoModes => [
    new("Centauri logo", [(Width, Height)], [4])
  ];

  /// <summary>The file's bytes, kept whole because the registers sit past the bitmap.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(CentauriLogoEditorFile file) {
    var data = file.Data ?? [];
    var c64 = Commodore64Graphics.CreatePalette();

    // Scattered rather than consecutive: the middle pair share a byte and the outer two do not.
    ReadOnlySpan<int> colors = [
      _At(data, FirstOffset) & 15,
      _At(data, PairOffset) >> 4,
      _At(data, PairOffset) & 15,
      _At(data, FourthOffset) & 15,
    ];

    var palette = new byte[colors.Length * 3];
    for (var i = 0; i < colors.Length; ++i)
      c64.AsSpan(colors[i] * 3, 3).CopyTo(palette.AsSpan(i * 3));

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Commodore64Graphics.DecodeFourColor(data, BitmapOffset, 0, Width, Height, palette),
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a logo, choosing four of the machine's colours for the whole picture.</summary>
  /// <remarks>
  /// Four colours for the entire screen rather than per cell, which is the trade this format makes:
  /// fewer colours than a multicolour picture, but no restriction on where they go. For a logo that
  /// is the better bargain, and it also makes the encoding simple — one global choice, then one
  /// register per logical pixel.
  /// <para/>
  /// The registers are written back to the three scattered bytes the C64 happens to keep them in:
  /// the middle pair share one byte's nibbles and the outer two have a byte each.
  /// </remarks>
  public static CentauriLogoEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var registers = _ChooseRegisters(rgb.PixelData);
    var data = new byte[FileSize];

    data[FirstOffset] = (byte)registers[0];
    data[PairOffset] = (byte)((registers[1] << 4) | registers[2]);
    data[FourthOffset] = (byte)registers[3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; x += 8) {
      var value = 0;
      for (var pixel = 0; pixel < 4; ++pixel) {
        var at = (y * Width + x + pixel * 2) * 3;
        value |= _Nearest(rgb.PixelData, at, registers) << (6 - pixel * 2);
      }

      data[BitmapOffset + (y & ~7) * Commodore64Graphics.Columns + x + (y & 7)] = (byte)value;
    }

    return new() { Data = data };
  }

  /// <summary>The four colours the whole picture is drawn in: the commonest the machine can show.</summary>
  private static int[] _ChooseRegisters(ReadOnlySpan<byte> rgb) {
    Span<int> totals = stackalloc int[Commodore64Graphics.ColorCount];
    for (var i = 0; i + 2 < rgb.Length; i += 3)
      ++totals[Commodore64Graphics.FindNearestColorIndex(rgb[i], rgb[i + 1], rgb[i + 2])];

    var registers = new int[4];
    for (var slot = 0; slot < registers.Length; ++slot) {
      var best = 0;
      for (var i = 1; i < Commodore64Graphics.ColorCount; ++i)
        if (totals[i] > totals[best])
          best = i;

      registers[slot] = best;
      totals[best] = -1;
    }

    return registers;
  }

  /// <summary>Which of the four registers a pixel is closest to.</summary>
  private static int _Nearest(ReadOnlySpan<byte> rgb, int pixel, int[] registers) {
    var palette = Commodore64Graphics.HexColors;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var slot = 0; slot < registers.Length; ++slot) {
      var color = palette[registers[slot]];
      long dr = rgb[pixel] - ((color >> 16) & 0xFF);
      long dg = rgb[pixel + 1] - ((color >> 8) & 0xFF);
      long db = rgb[pixel + 2] - (color & 0xFF);
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = slot;
    }

    return best;
  }
}
