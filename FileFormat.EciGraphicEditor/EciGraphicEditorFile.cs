using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EciGraphicEditor;

/// <summary>In-memory representation of a Commodore 64 ECI Graphic Editor (Extended Color Interlace) image by Crest.</summary>
public sealed class EciGraphicEditorFile : IImageFileFormat<EciGraphicEditorFile> {

  static string IImageFileFormat<EciGraphicEditorFile>.PrimaryExtension => ".eci";
  static string[] IImageFileFormat<EciGraphicEditorFile>.FileExtensions => [".eci", ".ecp"];
  static EciGraphicEditorFile IImageFileFormat<EciGraphicEditorFile>.FromFile(FileInfo file) => EciGraphicEditorReader.FromFile(file);
  static EciGraphicEditorFile IImageFileFormat<EciGraphicEditorFile>.FromBytes(byte[] data) => EciGraphicEditorReader.FromBytes(data);
  static EciGraphicEditorFile IImageFileFormat<EciGraphicEditorFile>.FromStream(Stream stream) => EciGraphicEditorReader.FromStream(stream);
  static byte[] IImageFileFormat<EciGraphicEditorFile>.ToBytes(EciGraphicEditorFile file) => EciGraphicEditorWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of a single bitmap section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Size of a single screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size: bitmap1 + screen1 + bitmap2 + screen2 + colorRam.</summary>
  internal const int MinPayloadSize = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize + ColorRamSize;

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
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this ECI image to a platform-independent <see cref="RawImage"/> in Rgb24 format using simplified multicolor interlace decode.</summary>
  public static RawImage ToRawImage(EciGraphicEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasBitmap2 = file.RawData.Length >= BitmapSize + ScreenRamSize + BitmapSize;
    var hasScreen2 = file.RawData.Length >= BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize;
    var hasColor = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;

        // Frame 1 decode
        var bitmap1Byte = bitmapOffset < BitmapSize && bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
        var pixelInByte = x % 4;
        var bitValue1 = (bitmap1Byte >> ((3 - pixelInByte) * 2)) & 0x03;

        int colorIndex1;
        var screen1Offset = BitmapSize + cellIndex;
        if (screen1Offset < file.RawData.Length) {
          var screenByte1 = file.RawData[screen1Offset];
          var colorByte = hasColor ? file.RawData[BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize + cellIndex] : (byte)0;

          colorIndex1 = bitValue1 switch {
            0 => 0,
            1 => (screenByte1 >> 4) & 0x0F,
            2 => screenByte1 & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0
          };
        } else
          colorIndex1 = bitValue1 != 0 ? 1 : 0;

        // Frame 2 decode (blend with frame 1)
        int colorIndex2;
        if (hasBitmap2) {
          var bitmap2Offset = BitmapSize + ScreenRamSize + bitmapOffset;
          var bitmap2Byte = bitmap2Offset < file.RawData.Length ? file.RawData[bitmap2Offset] : (byte)0;
          var bitValue2 = (bitmap2Byte >> ((3 - pixelInByte) * 2)) & 0x03;

          var screen2Offset = BitmapSize + ScreenRamSize + BitmapSize + cellIndex;
          if (hasScreen2 && screen2Offset < file.RawData.Length) {
            var screenByte2 = file.RawData[screen2Offset];
            var colorByte = hasColor ? file.RawData[BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize + cellIndex] : (byte)0;

            colorIndex2 = bitValue2 switch {
              0 => 0,
              1 => (screenByte2 >> 4) & 0x0F,
              2 => screenByte2 & 0x0F,
              3 => colorByte & 0x0F,
              _ => 0
            };
          } else
            colorIndex2 = bitValue2 != 0 ? 1 : 0;
        } else
          colorIndex2 = colorIndex1;

        // Blend the two frames by averaging
        var color1 = _C64Palette[colorIndex1];
        var color2 = _C64Palette[colorIndex2];
        var r = (byte)((((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2);
        var g = (byte)((((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2);
        var b = (byte)(((color1 & 0xFF) + (color2 & 0xFF)) / 2);

        var offset = (y * width + x) * 3;
        rgb[offset] = r;
        rgb[offset + 1] = g;
        rgb[offset + 2] = b;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. ECI images have complex interlace color switching constraints.</summary>
  public static EciGraphicEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to EciGraphicEditorFile is not supported due to complex interlace color switching constraints.");
  }
}
