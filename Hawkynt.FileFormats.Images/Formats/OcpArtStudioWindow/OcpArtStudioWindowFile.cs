using System;
using FileFormat.Core;

namespace FileFormat.OcpArtStudioWindow;

/// <summary>In-memory representation of an Advanced OCP Art Studio window (.win).</summary>
/// <remarks>
/// A rectangle cut out of an Amstrad mode 0 screen, with its size stored in the last five bytes
/// rather than the first — the program appended it after writing the picture, which is what a
/// clipping routine that does not know its own extent until it finishes naturally does.
/// <para/>
/// The colours are not in the file at all. They live in a .pal beside it, which also says which
/// screen mode the palette was made for; a window is only a window of a mode 0 screen, so a
/// palette naming any other mode belongs to a different picture.
/// </remarks>
public readonly record struct OcpArtStudioWindowFile
  : IImageFormatReader<OcpArtStudioWindowFile>, IImageToRawImage<OcpArtStudioWindowFile> {

  /// <summary>Colours a mode 0 screen shows.</summary>
  public const int ColorCount = 16;

  /// <summary>Bytes the size occupies at the end of the file.</summary>
  public const int TrailerLength = 5;

  /// <summary>The mode a window's palette must name.</summary>
  public const int RequiredMode = 0;

  static string IImageFormatMetadata<OcpArtStudioWindowFile>.PrimaryExtension => ".win";
  static string[] IImageFormatMetadata<OcpArtStudioWindowFile>.FileExtensions => [".win"];
  static OcpArtStudioWindowFile IImageFormatReader<OcpArtStudioWindowFile>.FromSpan(ReadOnlySpan<byte> data)
    => OcpArtStudioWindowReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<OcpArtStudioWindowFile>.VideoModes => [
    new("Window", [(new IntegerRange(1, 640), new IntegerRange(1, 200))], [ColorCount])
  ];

  /// <summary>The bitmap, unpacked if it was packed.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>The palette from the companion file, as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>
  /// Pixels across, which is half what the file stores — a mode 0 pixel occupies two of the
  /// machine's screen positions, and the stored width counts those.
  /// </summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Bytes one row occupies, which follows from the stored width rather than the pixel one.
  /// </summary>
  /// <remarks>
  /// Mode 0 fits two pixels in a byte, so a row of N pixels is N/2 bytes — but the header counts
  /// screen positions, of which there are 2N, and the row length is computed from those. The two
  /// halvings cancel, which is easy to do only once by mistake.
  /// </remarks>
  public int Stride { get; init; }

  public static RawImage ToRawImage(OcpArtStudioWindowFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = file.Stride;
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 2);
      var b = at < bitmap.Length ? bitmap[at] : 0;

      pixels[y * file.Width + x] = (byte)AmstradGraphics.Mode0Index(b, (x & 2) != 0);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette,
      PaletteCount = ColorCount,
    };
  }
}
