using System;
using FileFormat.Core;

namespace FileFormat.BbcMicroScreen;

/// <summary>In-memory representation of a BBC Micro screen dump.</summary>
/// <remarks>
/// All five bitmap modes share one memory layout: the screen is stored as character cells, so a
/// byte holds eight scanlines' worth of one cell column rather than a run of adjacent pixels. The
/// modes differ in how many bits each pixel takes and therefore how wide a cell is — and the bits
/// of a pixel are not adjacent, they are spread one per nibble-plane across the byte, which is why
/// each mode reassembles its index from scattered bits.
/// </remarks>
public readonly record struct BbcMicroScreenFile
  : IImageFormatReader<BbcMicroScreenFile>, IImageToRawImage<BbcMicroScreenFile>,
    IImageFromRawImage<BbcMicroScreenFile>, IImageFormatWriter<BbcMicroScreenFile> {

  /// <summary>Stored scanlines, the same in every mode.</summary>
  public const int ScreenRows = 256;

  /// <summary>The eight-colour hardware palette, repeated for the flashing half of the range.</summary>
  private static ReadOnlySpan<byte> _Palette => [
    0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x00,
    0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
  ];

  static string IImageFormatMetadata<BbcMicroScreenFile>.PrimaryExtension => ".bb4";
  static string[] IImageFormatMetadata<BbcMicroScreenFile>.FileExtensions => [".bb4", ".bb0", ".bb1", ".bb2", ".bb5"];
  static BbcMicroScreenFile IImageFormatReader<BbcMicroScreenFile>.FromSpan(ReadOnlySpan<byte> data)
    => BbcMicroScreenReader.FromSpan(data);
  static byte[] IImageFormatWriter<BbcMicroScreenFile>.ToBytes(BbcMicroScreenFile file)
    => BbcMicroScreenWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<BbcMicroScreenFile>.VideoModes => [
    new("Mode 4 (320x256 mono)", [(320, ScreenRows)], [2]),
    new("Mode 1 (320x256, 4 colours)", [(320, ScreenRows)], [4]),
    new("Mode 5 (160x256, 4 colours)", [(320, ScreenRows)], [4]),
    new("Mode 2 (160x256, 16 colours)", [(320, ScreenRows)], [16]),
    new("Mode 0 (640x512 mono)", [(640, 512)], [2]),
  ];

  /// <summary>Which display mode the bytes are laid out for.</summary>
  public BbcMicroMode Mode { get; init; }

  /// <summary>Raw screen bytes.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Bits each pixel occupies in the given mode.</summary>
  public static int BitsPerPixel(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 or BbcMicroMode.Mode4 => 1,
    BbcMicroMode.Mode1 or BbcMicroMode.Mode5 => 2,
    BbcMicroMode.Mode2 => 4,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>Bytes per row of character cells.</summary>
  public static int BytesPerCellRow(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 or BbcMicroMode.Mode1 or BbcMicroMode.Mode2 => 640,
    BbcMicroMode.Mode4 or BbcMicroMode.Mode5 => 320,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>File size for the given mode.</summary>
  public static int FileSizeFor(BbcMicroMode mode) => BytesPerCellRow(mode) * ScreenRows / 8;

  /// <summary>Stored pixels across a row, before any display doubling.</summary>
  public static int StoredWidth(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 => 640,
    BbcMicroMode.Mode1 or BbcMicroMode.Mode4 => 320,
    BbcMicroMode.Mode2 or BbcMicroMode.Mode5 => 160,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>Displayed width.</summary>
  public static int DisplayWidth(BbcMicroMode mode) => mode == BbcMicroMode.Mode0 ? 640 : 320;

  /// <summary>Displayed height.</summary>
  public static int DisplayHeight(BbcMicroMode mode) => mode == BbcMicroMode.Mode0 ? 512 : 256;

  /// <summary>Colours the mode can show.</summary>
  public static int ColorCount(BbcMicroMode mode) => 1 << BitsPerPixel(mode);

  /// <summary>Byte holding the given stored pixel, and the shift that selects its bit-plane.</summary>
  private static (int Offset, int Shift) _Locate(BbcMicroMode mode, int x, int y) {
    var cellRow = (y & ~7) * (BytesPerCellRow(mode) / 8);
    var lineInCell = y & 7;
    return BitsPerPixel(mode) switch {
      1 => (cellRow + (x & ~7) + lineInCell, ~x & 7),
      2 => (cellRow + ((x & ~3) << 1) + lineInCell, ~x & 3),
      _ => (cellRow + ((x & ~1) << 2) + lineInCell, ~x & 1),
    };
  }

  /// <summary>Reassembles a colour index from the bit-planes packed into one byte.</summary>
  private static int _Gather(BbcMicroMode mode, int b) => BitsPerPixel(mode) switch {
    1 => b & 1,
    2 => ((b >> 3) & 2) | (b & 1),
    _ => ((b >> 3) & 8) | ((b >> 2) & 4) | ((b >> 1) & 2) | (b & 1),
  };

  /// <summary>Spreads a colour index back across the bit-planes.</summary>
  private static int _Scatter(BbcMicroMode mode, int c) => BitsPerPixel(mode) switch {
    1 => c & 1,
    2 => ((c & 2) << 3) | (c & 1),
    _ => ((c & 8) << 3) | ((c & 4) << 2) | ((c & 2) << 1) | (c & 1),
  };

  public static RawImage ToRawImage(BbcMicroScreenFile file) {
    var mode = file.Mode;
    int width = DisplayWidth(mode), height = DisplayHeight(mode), stored = StoredWidth(mode);
    var colors = ColorCount(mode);

    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      var source = (colors == 2 ? (i == 0 ? 0 : 7) : i & 7) * 3;
      palette[i * 3] = _Palette[source];
      palette[i * 3 + 1] = _Palette[source + 1];
      palette[i * 3 + 2] = _Palette[source + 2];
    }

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceY = mode == BbcMicroMode.Mode0 ? y >> 1 : y;
      var sourceX = x * stored / width;
      var (offset, shift) = _Locate(mode, sourceX, sourceY);
      var b = offset < file.ScreenData.Length ? file.ScreenData[offset] >> shift : 0;
      pixels[y * width + x] = (byte)_Gather(mode, b);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = colors,
    };
  }

  public static BbcMicroScreenFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Mode 4 is the plain 320x256 monochrome screen and the natural conversion target.
    const BbcMicroMode mode = BbcMicroMode.Mode4;
    int width = DisplayWidth(mode), height = DisplayHeight(mode), stored = StoredWidth(mode);
    if (image.Width != width || image.Height != height)
      throw new ArgumentException($"Expected {width}x{height} but got {image.Width}x{image.Height}.", nameof(image));

    var grey = PixelConverter.Convert(image, PixelFormat.Gray8);
    var data = new byte[FileSizeFor(mode)];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < stored; ++x) {
      if (grey.PixelData[y * width + x * width / stored] < 128)
        continue;

      var (offset, shift) = _Locate(mode, x, y);
      data[offset] |= (byte)(_Scatter(mode, 1) << shift);
    }

    return new() { Mode = mode, ScreenData = data };
  }
}
