using System;
using FileFormat.Core;

namespace FileFormat.MsxSprite;

/// <summary>In-memory representation of an MSX sprite pattern table (2048 bytes: 32 sprites x 8 bytes each, 8x8 mono).</summary>
public readonly record struct MsxSpriteFile
  : IImageFormatReader<MsxSpriteFile>, IImageToRawImage<MsxSpriteFile>,
    IImageFromRawImage<MsxSpriteFile>, IImageFormatWriter<MsxSpriteFile> {

  static string IImageFormatMetadata<MsxSpriteFile>.PrimaryExtension => ".spt";
  static string[] IImageFormatMetadata<MsxSpriteFile>.FileExtensions => [".spt"];
  static MsxSpriteFile IImageFormatReader<MsxSpriteFile>.FromSpan(ReadOnlySpan<byte> data) => MsxSpriteReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MsxSpriteFile>.VideoModes => [new("Default", [(PixelWidth, PixelHeight)], [2])];
  static byte[] IImageFormatWriter<MsxSpriteFile>.ToBytes(MsxSpriteFile file) => MsxSpriteWriter.ToBytes(file);

  /// <summary>Expected file size in bytes.</summary>
  internal const int ExpectedFileSize = 2048;

  /// <summary>Number of sprites in the pattern table.</summary>
  internal const int SpriteCount = 32;

  /// <summary>Bytes per sprite (8x8 mono = 8 bytes).</summary>
  internal const int BytesPerSprite = 8;

  /// <summary>Sprite width in pixels.</summary>
  internal const int SpriteWidth = 8;

  /// <summary>Sprite height in pixels.</summary>
  internal const int SpriteHeight = 8;

  /// <summary>Output image width: 16 sprites per row x 8 pixels = 128.</summary>
  internal const int PixelWidth = 128;

  /// <summary>Output image height: 2 rows x 8 pixels = 16.</summary>
  internal const int PixelHeight = 16;

  /// <summary>Sprites per row in the rendered grid.</summary>
  private const int _SPRITES_PER_ROW = 16;

  /// <summary>Always 128.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 16.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw sprite pattern data (2048 bytes).</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the MSX sprite table to an Indexed1 raw image (128x16, B&amp;W palette).</summary>
  public static RawImage ToRawImage(MsxSpriteFile file) {

    var rowStride = PixelWidth / 8;
    var pixelData = new byte[rowStride * PixelHeight];

    for (var spriteIndex = 0; spriteIndex < SpriteCount; ++spriteIndex) {
      var gridCol = spriteIndex % _SPRITES_PER_ROW;
      var gridRow = spriteIndex / _SPRITES_PER_ROW;
      var baseX = gridCol * SpriteWidth;
      var baseY = gridRow * SpriteHeight;

      for (var row = 0; row < SpriteHeight; ++row) {
        var dataOffset = spriteIndex * BytesPerSprite + row;
        var spriteByte = dataOffset < file.RawData.Length ? file.RawData[dataOffset] : (byte)0;

        for (var bit = 0; bit < SpriteWidth; ++bit) {
          if (((spriteByte >> (7 - bit)) & 1) == 0)
            continue;

          var px = baseX + bit;
          var py = baseY + row;
          var byteIndex = py * rowStride + px / 8;
          var bitIndex = 7 - (px % 8);
          pixelData[byteIndex] |= (byte)(1 << bitIndex);
        }
      }
    }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Builds a sprite pattern table from a <see cref="RawImage"/> holding the fixed 16x2 grid of
  /// 8x8 sprites this format renders as. Each pixel is thresholded to black or white. The file always
  /// reserves the full 2048-byte pattern generator table the hardware addresses; only the first 32
  /// patterns carry picture data; the rest comes back zeroed.</summary>
  public static MsxSpriteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.SampleTo(PixelWidth, PixelHeight);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed1, _BlackWhitePalette);
    var rowStride = PixelWidth / 8;
    var data = new byte[ExpectedFileSize];

    for (var spriteIndex = 0; spriteIndex < SpriteCount; ++spriteIndex) {
      var gridCol = spriteIndex % _SPRITES_PER_ROW;
      var gridRow = spriteIndex / _SPRITES_PER_ROW;
      var baseX = gridCol * SpriteWidth;
      var baseY = gridRow * SpriteHeight;

      for (var row = 0; row < SpriteHeight; ++row) {
        byte spriteByte = 0;
        for (var bit = 0; bit < SpriteWidth; ++bit) {
          var px = baseX + bit;
          var py = baseY + row;
          var byteIndex = py * rowStride + px / 8;
          var bitIndex = 7 - (px % 8);
          if (((indexed.PixelData[byteIndex] >> bitIndex) & 1) != 0)
            spriteByte |= (byte)(1 << (7 - bit));
        }

        data[spriteIndex * BytesPerSprite + row] = spriteByte;
      }
    }

    return new() { RawData = data };
  }

}
