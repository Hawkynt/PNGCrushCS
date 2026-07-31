using System;
using FileFormat.Core;

namespace FileFormat.DegasIcon;

/// <summary>In-memory representation of a DEGAS Elite icon (.icn).</summary>
/// <remarks>
/// Not a binary file at all but a fragment of C source, the way DEGAS Elite exported an icon for a
/// programmer to paste into a program: three <c>#define</c>s giving the width, the height and the
/// number of words, and then an initialiser of hexadecimal words holding the bitmap. Everything is
/// one bit a pixel, and a row is padded out to a whole word.
/// <para/>
/// The extension is shared with the ICE character editor's output, which is binary, so the two are
/// told apart by whether the file parses as C.
/// </remarks>
public readonly record struct DegasIconFile
  : IImageFormatReader<DegasIconFile>, IImageToRawImage<DegasIconFile> {

  static string IImageFormatMetadata<DegasIconFile>.PrimaryExtension => ".icn";
  static string[] IImageFormatMetadata<DegasIconFile>.FileExtensions => [".icn"];
  static DegasIconFile IImageFormatReader<DegasIconFile>.FromSpan(ReadOnlySpan<byte> data)
    => DegasIconReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DegasIconFile>.VideoModes => [
    new("Atari ST", [(new(1, 255), new(1, 255))], [2])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The bitmap, one bit a pixel with each row padded out to a whole word.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(DegasIconFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = (file.Width + 15) >> 4 << 1;
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 3);

      // A set bit is ink on a white ground, which is what an icon on a desktop looks like.
      pixels[y * file.Width + x] = (byte)(at < bitmap.Length ? (bitmap[at] >> (~x & 7)) & 1 : 0);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };
  }
}
