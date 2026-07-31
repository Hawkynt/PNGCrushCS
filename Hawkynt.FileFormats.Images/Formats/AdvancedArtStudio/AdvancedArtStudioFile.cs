using System;
using FileFormat.Core;

namespace FileFormat.AdvancedArtStudio;

/// <summary>Advanced Art Studio (.ocp) C64 image. Supports both multicolor and hi-res screen layouts.</summary>
public readonly record struct AdvancedArtStudioFile : IImageFormatReader<AdvancedArtStudioFile>, IImageToRawImage<AdvancedArtStudioFile>, IImageFromRawImage<AdvancedArtStudioFile>, IImageFormatWriter<AdvancedArtStudioFile> {

  static string IImageFormatMetadata<AdvancedArtStudioFile>.PrimaryExtension => ".ocp";
  static string[] IImageFormatMetadata<AdvancedArtStudioFile>.FileExtensions => [".ocp"];
  static AdvancedArtStudioFile IImageFormatReader<AdvancedArtStudioFile>.FromSpan(ReadOnlySpan<byte> data) => AdvancedArtStudioReader.FromSpan(data);
  static byte[] IImageFormatWriter<AdvancedArtStudioFile>.ToBytes(AdvancedArtStudioFile file) => AdvancedArtStudioWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AdvancedArtStudioFile>.VideoModes => [
    new("Multicolor", [(160, 200)], [16]),
    new("Hi-Res",     [(320, 200)], [16]),
  ];

  /// <summary>Fixed dimensions for the multicolor mode (16 colours per cell, double-wide pixels).</summary>
  public const int MulticolorWidth = 160;
  public const int HiResWidth = 320;
  public const int FixedHeight = 200;

  /// <summary>Multicolor file size: 2 (load addr) + 8000 (bitmap) + 1000 (screen) + 1000 (colour) + 16 (trailing).</summary>
  public const int MulticolorFileSize = 10018;
  /// <summary>Hi-res file size: 2 (load addr) + 8000 (bitmap) + 1000 (screen) + 7 (trailing).</summary>
  public const int HiResFileSize = 9009;

  /// <summary>Kept for source compatibility with callers that referenced the original multicolor-only constant.</summary>
  public const int FixedWidth = MulticolorWidth;
  /// <summary>Kept for source compatibility with callers that referenced the original multicolor-only constant.</summary>
  public const int ExpectedFileSize = MulticolorFileSize;

  internal const int BitmapDataSize = 8000;
  internal const int ScreenRamSize = 1000;
  internal const int ColorRamSize = 1000;
  internal const int LoadAddressSize = 2;
  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = LoadAddressSize;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = BitmapOffset + BitmapDataSize;

  /// <summary>
  /// Where the shared background register sits. It is not at the end of the file: a sixteen-byte
  /// block sits between the video matrix and the colour RAM, and the register lives inside it.
  /// </summary>
  public const int MulticolorBackgroundOffset = 9003;

  /// <summary>Where the colour RAM starts, after that block.</summary>
  public const int MulticolorColorRamOffset = 9018;

  internal const int MulticolorTrailingSize = 16;
  internal const int HiResTrailingSize = 7;

  /// <summary><c>true</c> for the 320x200 hi-res variant; <c>false</c> for the 160x200 multicolor variant.</summary>
  public bool IsHiRes { get; init; }

  public int Width => IsHiRes ? HiResWidth : MulticolorWidth;
  public int Height => FixedHeight;

  public ushort LoadAddress { get; init; }
  public byte[] BitmapData { get; init; }
  public byte[] ScreenRam { get; init; }
  /// <summary>Colour RAM (1000 bytes) — multicolor mode only; an empty array in hi-res mode.</summary>
  public byte[] ColorRam { get; init; }
  public byte BackgroundColor { get; init; }
  public byte BorderColor { get; init; }

  public static RawImage ToRawImage(AdvancedArtStudioFile file) =>
    file.IsHiRes ? _ToRawImageHiRes(file) : _ToRawImageMulticolor(file);

  private static RawImage _ToRawImageMulticolor(AdvancedArtStudioFile file) {
    const int width = MulticolorWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenRam[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenRam[cellIndex] & 0x0F,
          3 => file.ColorRam[cellIndex] & 0x0F,
          _ => 0
        };

        _PutPixel(rgb, y * width + x, colorIndex);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static RawImage _ToRawImageHiRes(AdvancedArtStudioFile file) {
    const int width = HiResWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 8;
        var bit = (bitmapByte >> (7 - pixelInByte)) & 1;
        var screen = file.ScreenRam[cellIndex];
        var colorIndex = bit == 1 ? (screen >> 4) & 0x0F : screen & 0x0F;

        _PutPixel(rgb, y * width + x, colorIndex);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static void _PutPixel(byte[] rgb, int pixelIndex, int colorIndex) {
    var color = Commodore64Graphics.HexColors[colorIndex];
    var o = pixelIndex * 3;
    rgb[o]     = (byte)((color >> 16) & 0xFF);
    rgb[o + 1] = (byte)((color >> 8) & 0xFF);
    rgb[o + 2] = (byte)(color & 0xFF);
  }

  /// <summary>Builds a multicolour screen, which is the form that holds the most colour.</summary>
  public static AdvancedArtStudioFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(MulticolorWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[ScreenRamSize];
    var colors = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, MulticolorWidth, FixedHeight, bitmap, matrix, colors);

    return new() {
      IsHiRes = false,
      LoadAddress = 0x2000,
      BitmapData = bitmap,
      ScreenRam = matrix,
      ColorRam = colors,
      BackgroundColor = background,
      BorderColor = background,
    };
  }
}
