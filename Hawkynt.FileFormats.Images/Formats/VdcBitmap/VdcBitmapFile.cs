using System;
using FileFormat.Core;

namespace FileFormat.VdcBitmap;

/// <summary>In-memory representation of a VDC BitMap (.bm, .vbm).</summary>
/// <remarks>
/// The Commodore 128's second video chip drove an eighty-column monochrome display, and this is a
/// bitmap for it: a size and then one bit per pixel. Two versions exist and they disagree about
/// which way round the two colours go — version 2 draws ink on white and version 3 draws it on
/// black, so reading one as the other produces a photographic negative rather than an error.
/// <para/>
/// Version 3 also allows a run-length encoding whose escape bytes are not fixed but chosen per
/// file and listed in the header, so that a picture can reserve whichever byte values it happens
/// not to use as literals.
/// </remarks>
public readonly record struct VdcBitmapFile
  : IImageFormatReader<VdcBitmapFile>, IImageToRawImage<VdcBitmapFile> {

  /// <summary>The three bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [(byte)'B', (byte)'M', 0xCB];

  /// <summary>Offset of a version 2 bitmap.</summary>
  public const int Version2BitmapOffset = 8;

  /// <summary>Offset of the escape byte table in a version 3 file.</summary>
  public const int EscapeOffset = 9;

  /// <summary>Escape bytes a version 3 file names.</summary>
  public const int EscapeCount = 5;

  static string IImageFormatMetadata<VdcBitmapFile>.PrimaryExtension => ".vbm";
  static string[] IImageFormatMetadata<VdcBitmapFile>.FileExtensions => [".vbm", ".bm"];
  static VdcBitmapFile IImageFormatReader<VdcBitmapFile>.FromSpan(ReadOnlySpan<byte> data)
    => VdcBitmapReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<VdcBitmapFile>.VideoModes => [
    new("VDC", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  /// <summary>The bitmap, one bit per pixel with rows starting on a byte boundary.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Whether a set bit is black on white rather than white on black.</summary>
  public bool InkIsBlack { get; init; }

  public static RawImage ToRawImage(VdcBitmapFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = (file.Width + 7) >> 3;
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 3);
      if (at < bitmap.Length && ((bitmap[at] >> (~x & 7)) & 1) != 0)
        pixels[y * file.Width + x] = 1;
    }

    var background = file.InkIsBlack ? (byte)255 : (byte)0;
    var ink = (byte)(background ^ 255);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [background, background, background, ink, ink, ink],
      PaletteCount = 2,
    };
  }
}
