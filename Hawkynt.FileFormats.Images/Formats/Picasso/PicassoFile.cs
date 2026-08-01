using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Picasso;

/// <summary>In-memory representation of a Picasso picture (.pic0, with a .pic1 beside it).</summary>
/// <remarks>
/// A VIC-20 multicolour screen of 176x176 in cells sixteen scanlines deep. The two files divide the
/// picture from its colours — the .pic0 holds the bitmap and the two screen-wide colours, the .pic1
/// the per-cell one — which is how the machine itself held them, in two areas of memory a program
/// could point the chip at independently.
/// <para/>
/// Every cell's colour byte must have its multicolour bit set, because a picture using both
/// character modes at once is not one this program made.
/// </remarks>
public readonly record struct PicassoFile
  : IImageFormatReader<PicassoFile>, IImageToRawImage<PicassoFile> {

  /// <summary>Pixels across and down.</summary>
  public const int Size = 176;

  /// <summary>Cells across.</summary>
  public const int Columns = Size / 8;

  /// <summary>Scanlines a cell spans.</summary>
  public const int CellHeight = 16;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Offset of the byte holding the border and one of the shared colours.</summary>
  public const int AuxiliaryOffset = 3888;

  /// <summary>Offset of the byte holding the background and the other shared colour.</summary>
  public const int BackgroundOffset = 3889;

  /// <summary>Total size of the bitmap file.</summary>
  public const int FileSize = 3890;

  /// <summary>Size of the companion holding the per-cell colours.</summary>
  public const int ColorFileSize = 244;

  /// <summary>Offset of the per-cell colours within the companion.</summary>
  public const int ColorsOffset = 2;

  static string IImageFormatMetadata<PicassoFile>.PrimaryExtension => ".pic0";
  static string[] IImageFormatMetadata<PicassoFile>.FileExtensions => [".pic0"];
  static PicassoFile IImageFormatReader<PicassoFile>.FromSpan(ReadOnlySpan<byte> data)
    => PicassoReader.FromSpan(data);

  /// <summary>Reads the file together with the companion it cannot be shown without.</summary>
  static PicassoFile IImageFormatReader<PicassoFile>.FromFile(FileInfo file)
    => PicassoReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<PicassoFile>.VideoModes => [
    new("Picasso", [(Size, Size)], [Vic20Graphics.ColorCount])
  ];

  /// <summary>The bitmap file.</summary>
  public byte[] Data { get; init; }

  /// <summary>The per-cell colours from the companion file.</summary>
  public byte[] Colors { get; init; }

  public static RawImage ToRawImage(PicassoFile file) {
    var data = file.Data ?? [];
    var colors = file.Colors ?? [];
    var pixels = new byte[Size * Size];

    for (var y = 0; y < Size; ++y)
    for (var x = 0; x < Size; ++x) {
      var cell = (y / CellHeight) * Columns + (x >> 3);
      var ink = cell + ColorsOffset < colors.Length ? colors[cell + ColorsOffset] : 0;

      var at = BitmapOffset + (cell << 4) + (y & 15);
      var pattern = at < data.Length ? (data[at] >> (~x & 6)) & 3 : 0;

      pixels[y * Size + x] = (byte)(pattern switch {
        0 => data[BackgroundOffset] >> 4,
        1 => data[BackgroundOffset] & 7,
        2 => ink & 7,
        _ => data[AuxiliaryOffset] >> 4,
      });
    }

    return new() {
      Width = Size,
      Height = Size,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Vic20Graphics.CreatePalette(),
      PaletteCount = Vic20Graphics.ColorCount,
    };
  }
}
