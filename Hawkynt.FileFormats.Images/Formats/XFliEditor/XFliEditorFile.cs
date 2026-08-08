using System;
using FileFormat.Core;

namespace FileFormat.XFliEditor;

/// <summary>In-memory representation of a C64 X-FLI Editor (.xfl) extended FLI multicolor image.</summary>
public readonly record struct XFliEditorFile
  : IImageFormatReader<XFliEditorFile>, IImageToRawImage<XFliEditorFile>,
    IImageFromRawImage<XFliEditorFile>, IImageFormatWriter<XFliEditorFile> {

  static string IImageFormatMetadata<XFliEditorFile>.PrimaryExtension => ".xfl";
  static string[] IImageFormatMetadata<XFliEditorFile>.FileExtensions => [".xfl"];
  static XFliEditorFile IImageFormatReader<XFliEditorFile>.FromSpan(ReadOnlySpan<byte> data) => XFliEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<XFliEditorFile>.ToBytes(XFliEditorFile file) => XFliEditorWriter.ToBytes(file);

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of one screen RAM bank in bytes.</summary>
  internal const int ScreenBankSize = 1000;

  /// <summary>Number of screen banks in FLI mode.</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>Size of all screen banks combined.</summary>
  internal const int AllScreenBanksSize = ScreenBankSize * ScreenBankCount; // 8000

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum raw payload size: bitmap + 8 screen banks + color RAM.</summary>
  internal const int MinPayloadSize = BitmapDataSize + AllScreenBanksSize + ColorDataSize; // 17000

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int ImageWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int ImageHeight = 200;

  /// <summary>Default load address, putting the bitmap at $2000.</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>8 screen RAM banks, each 1000 bytes. ScreenBanks[bank][cellIndex].</summary>
  public byte[][] ScreenBanks { get; init; }

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; }

  /// <summary>Background color index (0-15). Bit-pair 0 maps to this color.</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; }

  /// <summary>Converts this X-FLI Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(XFliEditorFile file) {

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var bank = y % ScreenBankCount;
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitPair = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var screenByte = file.ScreenBanks[bank][cellIndex];
        var colorIndex = bitPair switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (screenByte >> 4) & 0x0F,
          2 => screenByte & 0x0F,
          3 => file.ColorData[cellIndex] & 0x0F,
          _ => 0
        };

        var color = Commodore64Graphics.HexColors[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Encodes a picture as an X-FLI screen, scaling it to 160x200 first.</summary>
  /// <remarks>
  /// Unlike the bare FLI formats this one keeps a background register of its own, so pattern 00 can
  /// be spent on the picture's commonest colour instead of being stuck on black. The eight video
  /// matrices are filled a raster line at a time and colour memory stays shared across each cell,
  /// which is how <see cref="ToRawImage"/> reads them back.
  /// </remarks>
  public static XFliEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];
    var screens = new byte[AllScreenBanksSize];
    var color = new byte[ColorDataSize];

    // Only the shared register is chosen by frequency here; the per-cell work is the encoder's.
    var background = _CommonestColor(rgb);
    Commodore64Graphics.EncodeMulticolorFli(
      rgb, ImageWidth, ImageHeight, background, bitmap, screens, ScreenBankSize, color);

    var banks = new byte[ScreenBankCount][];
    for (var i = 0; i < ScreenBankCount; ++i) {
      banks[i] = new byte[ScreenBankSize];
      screens.AsSpan(i * ScreenBankSize, ScreenBankSize).CopyTo(banks[i]);
    }

    return new() {
      LoadAddress = DefaultLoadAddress,
      BitmapData = bitmap,
      ScreenBanks = banks,
      ColorData = color,
      BackgroundColor = background,
      TrailingData = [],
    };
  }

  /// <summary>The machine colour the picture uses most, which is what the shared register is worth spending on.</summary>
  private static byte _CommonestColor(ReadOnlySpan<byte> rgb) {
    Span<int> totals = stackalloc int[Commodore64Graphics.ColorCount];
    for (var at = 0; at + 2 < rgb.Length; at += 3)
      ++totals[Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2])];

    var best = 0;
    for (var i = 1; i < totals.Length; ++i)
      if (totals[i] > totals[best])
        best = i;

    return (byte)best;
  }

}
