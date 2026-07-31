using System;
using FileFormat.Core;

namespace FileFormat.MiniPaint;

/// <summary>In-memory representation of a MINIPAINT picture (.mg).</summary>
/// <remarks>
/// A VIC-20 screen that mixes its two graphics modes cell by cell. Each sixteen-pixel-wide cell
/// carries a colour nibble, and the top bit of that nibble decides how the cell's bitmap is read:
/// set means two bits a pixel against four colours at half the horizontal resolution, clear means
/// one bit a pixel against two at full. So a picture can spend detail where it has edges and colour
/// where it has areas, which is more than either mode offers alone.
/// <para/>
/// The bitmap runs column by column — a whole column of 192 rows before the next — which is the
/// order a redefined character set occupies memory. A separate bit inverts the two-colour cells,
/// so the same bitmap can read either way round.
/// </remarks>
public readonly record struct MiniPaintFile
  : IImageFormatReader<MiniPaintFile>, IImageToRawImage<MiniPaintFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 160;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>
  /// The BASIC stub every file starts with, which loads and runs the picture and is what identifies
  /// the format — there is no signature of its own.
  /// </summary>
  public static ReadOnlySpan<byte> Signature => [
    241, 16, 12, 18, 216, 7, 158, 32, (byte)'8', (byte)'5', (byte)'8', (byte)'4', 0, 0, 0,
  ];

  /// <summary>Offset of the byte holding the colour the two-colour cells draw their ink from.</summary>
  public const int InkOffset = 15;

  /// <summary>Offset of the byte holding the background, the border and the inversion bit.</summary>
  public const int ControlOffset = 16;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 17;

  /// <summary>Offset of the per-cell colour nibbles.</summary>
  public const int ColorsOffset = 3857;

  /// <summary>Total file size.</summary>
  public const int FileSize = 4097;

  static string IImageFormatMetadata<MiniPaintFile>.PrimaryExtension => ".mg";
  static string[] IImageFormatMetadata<MiniPaintFile>.FileExtensions => [".mg"];
  static MiniPaintFile IImageFormatReader<MiniPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => MiniPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MiniPaintFile>.VideoModes => [
    new("MINIPAINT", [(Width, Height)], [Vic20Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(MiniPaintFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    Span<byte> colors = stackalloc byte[4];
    colors[0] = (byte)(data[ControlOffset] >> 4);
    colors[1] = (byte)(data[ControlOffset] & 7);
    colors[3] = (byte)(data[InkOffset] >> 4);

    // One bit of the control byte says which way round the two-colour cells read.
    var invert = ~(data[ControlOffset] >> 3) & 1;

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var cell = data[ColorsOffset + (y >> 4) * 10 + (x >> 4)];
      var color = (cell >> ((x >> 1) & 4)) & 15;
      var bits = data[BitmapOffset + (x >> 3) * Height + y];

      int index;
      if (color >= 8) {
        colors[2] = (byte)(color & 7);
        index = (bits >> (~x & 6)) & 3;
      } else {
        colors[2] = (byte)color;
        index = (((bits >> (~x & 7)) & 1) ^ invert) << 1;
      }

      pixels[y * Width + x] = colors[index];
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Vic20Graphics.CreatePalette(),
      PaletteCount = Vic20Graphics.ColorCount,
    };
  }
}
