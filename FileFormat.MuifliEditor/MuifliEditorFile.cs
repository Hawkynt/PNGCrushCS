using System;
using FileFormat.Core;

namespace FileFormat.MuifliEditor;

/// <summary>In-memory representation of a C64 MUIFLI (Multicolor Unrestricted FLI Interlace) image.</summary>
public readonly record struct MuifliEditorFile : IImageFormatReader<MuifliEditorFile>, IImageToRawImage<MuifliEditorFile>, IImageFormatWriter<MuifliEditorFile> {

  static string IImageFormatMetadata<MuifliEditorFile>.PrimaryExtension => ".muf";
  static string[] IImageFormatMetadata<MuifliEditorFile>.FileExtensions => [".muf", ".mui", ".mup"];
  static MuifliEditorFile IImageFormatReader<MuifliEditorFile>.FromSpan(ReadOnlySpan<byte> data) => MuifliEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<MuifliEditorFile>.ToBytes(MuifliEditorFile file) => MuifliEditorWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Number of interlace frames.</summary>
  internal const int FrameCount = 2;

  /// <summary>Size of the bitmap data section per frame in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Number of screen RAM banks per frame (one per char row group for FLI).</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>Size of each screen RAM bank in bytes (1024 for MUIFLI).</summary>
  internal const int ScreenBankSize = 1024;

  /// <summary>Total size of all screen RAM banks per frame.</summary>
  internal const int TotalScreenSize = ScreenBankCount * ScreenBankSize;

  /// <summary>Size of the color RAM section per frame in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of a single frame (bitmap + 8 screen banks + color RAM).</summary>
  internal const int FrameSize = BitmapSize + TotalScreenSize + ColorRamSize;

  /// <summary>Minimum payload size (2 frames).</summary>
  internal const int MinPayloadSize = FrameCount * FrameSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this MUIFLI Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format by decoding two interlace frames and averaging their colors.</summary>
  public static RawImage ToRawImage(MuifliEditorFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasFullData = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        int rSum = 0, gSum = 0, bSum = 0;
        var framesDone = 0;

        for (var frame = 0; frame < FrameCount; ++frame) {
          var frameBase = frame * FrameSize;
          if (frameBase + FrameSize > file.RawData.Length && hasFullData)
            continue;

          var cellX = x / 4;
          var cellY = y / 8;
          var cellIndex = cellY * 40 + cellX;
          var byteInCell = y % 8;
          var bitmapOffset = frameBase + cellIndex * 8 + byteInCell;
          var bitmapByte = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
          var pixelInByte = x % 4;
          var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

          int colorIndex;
          if (hasFullData) {
            var screenBank = byteInCell % ScreenBankCount;
            var screenOffset = frameBase + BitmapSize + screenBank * ScreenBankSize + cellIndex;
            var screenByte = screenOffset < file.RawData.Length ? file.RawData[screenOffset] : (byte)0;
            var colorOffset = frameBase + BitmapSize + TotalScreenSize + cellIndex;
            var colorByte = colorOffset < file.RawData.Length ? file.RawData[colorOffset] : (byte)0;

            colorIndex = bitValue switch {
              0 => 0,
              1 => (screenByte >> 4) & 0x0F,
              2 => screenByte & 0x0F,
              3 => colorByte & 0x0F,
              _ => 0
            };
          } else
            colorIndex = bitValue != 0 ? 1 : 0;

          var color = _C64Palette[colorIndex];
          rSum += (color >> 16) & 0xFF;
          gSum += (color >> 8) & 0xFF;
          bSum += color & 0xFF;
          ++framesDone;
        }

        if (framesDone == 0)
          framesDone = 1;

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)(rSum / framesDone);
        rgb[offset + 1] = (byte)(gSum / framesDone);
        rgb[offset + 2] = (byte)(bSum / framesDone);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
