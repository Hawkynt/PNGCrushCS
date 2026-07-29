using System;
using FileFormat.Core;

namespace FileFormat.HiResEditor;

/// <summary>In-memory representation of a C64 Hires-Editor (.het) or Run Paint (.rph) picture.</summary>
/// <remarks>
/// The standard C64 high-resolution layout: a load address, the 1000-byte video matrix, a gap up to
/// the next kilobyte boundary, then the 8000-byte bitmap. One bit per pixel selects between two
/// colours per 8x8 cell, and the video matrix byte holds both — foreground in the high nibble,
/// background in the low one. Hires-Editor and Run Paint write the same bytes and differ only in
/// the extension.
/// </remarks>
public readonly record struct HiResEditorFile
  : IImageFormatReader<HiResEditorFile>, IImageToRawImage<HiResEditorFile>,
    IImageFromRawImage<HiResEditorFile>, IImageFormatWriter<HiResEditorFile> {

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Offset of the video matrix, immediately after the load address.</summary>
  public const int ScreenDataOffset = LoadAddressSize;

  /// <summary>Size of the video matrix in bytes (40x25 cells).</summary>
  public const int ScreenDataSize = 1000;

  /// <summary>Offset of the bitmap: the video matrix is padded up to a kilobyte.</summary>
  public const int BitmapDataOffset = LoadAddressSize + 1024;

  /// <summary>Size of the bitmap in bytes (320x200 at one bit per pixel).</summary>
  public const int BitmapDataSize = 8000;

  /// <summary>Expected total file size.</summary>
  public const int ExpectedFileSize = BitmapDataOffset + BitmapDataSize;

  /// <summary>Default load address, putting the bitmap at $6000.</summary>
  internal const ushort DefaultLoadAddress = 0x5C00;

  /// <summary>Image width in pixels.</summary>
  public const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  public const int PixelHeight = 200;

  /// <summary>Cells across the screen.</summary>
  public const int Columns = PixelWidth / 8;

  /// <summary>Cells down the screen.</summary>
  public const int Rows = PixelHeight / 8;

  /// <summary>Colours the machine offers.</summary>
  public const int ColorCount = 16;

  /// <summary>The fixed C64 16-colour palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  static string IImageFormatMetadata<HiResEditorFile>.PrimaryExtension => ".het";
  static string[] IImageFormatMetadata<HiResEditorFile>.FileExtensions => [".het", ".rph"];
  static HiResEditorFile IImageFormatReader<HiResEditorFile>.FromSpan(ReadOnlySpan<byte> data) => HiResEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiResEditorFile>.ToBytes(HiResEditorFile file) => HiResEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HiResEditorFile>.VideoModes => [
    new("Hires", [(PixelWidth, PixelHeight)], [ColorCount])
  ];

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data, one bit per pixel, stored cell by cell.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix: high nibble is the set-bit colour, low nibble the clear-bit colour.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>The palette as RGB triplets.</summary>
  internal static byte[] PaletteRgb() {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      palette[i * 3] = (byte)((_C64Palette[i] >> 16) & 0xFF);
      palette[i * 3 + 1] = (byte)((_C64Palette[i] >> 8) & 0xFF);
      palette[i * 3 + 2] = (byte)(_C64Palette[i] & 0xFF);
    }

    return palette;
  }

  public static RawImage ToRawImage(HiResEditorFile file) {
    var bitmap = file.BitmapData ?? [];
    var screen = file.ScreenData ?? [];
    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
    for (var x = 0; x < PixelWidth; ++x) {
      var cell = (y / 8) * Columns + (x / 8);
      var index = cell * 8 + (y % 8);
      var bit = index < bitmap.Length ? (bitmap[index] >> (7 - (x % 8))) & 1 : 0;
      var attribute = cell < screen.Length ? screen[cell] : (byte)0;
      pixels[y * PixelWidth + x] = (byte)(bit == 1 ? (attribute >> 4) & 0x0F : attribute & 0x0F);
    }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = PaletteRgb(),
      PaletteCount = ColorCount,
    };
  }

  public static HiResEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // The palette is the machine's, so every pixel is mapped into it before anything else.
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var palette = PaletteRgb();
    var mapped = ColorQuantizer.MapToPalette(bgra.PixelData, PixelWidth * PixelHeight, palette);

    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];

    for (var row = 0; row < Rows; ++row)
    for (var col = 0; col < Columns; ++col) {
      var cell = row * Columns + col;

      // A cell can show two of the sixteen colours; take the two most common in it, which is
      // exactly optimal once every pixel has already been mapped into the palette.
      Span<int> counts = stackalloc int[ColorCount];
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        ++counts[mapped.Indices[(row * 8 + y) * PixelWidth + col * 8 + x]];

      var foreground = _Dominant(counts, -1);
      var background = _Dominant(counts, foreground);
      screen[cell] = (byte)((foreground << 4) | background);

      for (var y = 0; y < 8; ++y) {
        var bits = 0;
        for (var x = 0; x < 8; ++x) {
          var index = mapped.Indices[(row * 8 + y) * PixelWidth + col * 8 + x];
          // Anything that is neither of the cell's two colours goes to whichever is nearer.
          if (index == foreground || (index != background && _IsCloser(palette, index, foreground, background)))
            bits |= 0x80 >> x;
        }

        bitmap[cell * 8 + y] = (byte)bits;
      }
    }

    return new() { LoadAddress = DefaultLoadAddress, BitmapData = bitmap, ScreenData = screen };
  }

  /// <summary>The most used colour in a cell, ignoring one already taken.</summary>
  private static int _Dominant(ReadOnlySpan<int> counts, int taken) {
    var best = taken == 0 ? 1 : 0;
    for (var i = 0; i < counts.Length; ++i)
      if (i != taken && counts[i] > counts[best])
        best = i;

    return best;
  }

  private static bool _IsCloser(byte[] palette, int index, int first, int second)
    => _Distance(palette, index, first) <= _Distance(palette, index, second);

  private static int _Distance(byte[] palette, int a, int b) {
    int dr = palette[a * 3] - palette[b * 3];
    int dg = palette[a * 3 + 1] - palette[b * 3 + 1];
    int db = palette[a * 3 + 2] - palette[b * 3 + 2];

    return dr * dr + dg * dg + db * db;
  }
}
