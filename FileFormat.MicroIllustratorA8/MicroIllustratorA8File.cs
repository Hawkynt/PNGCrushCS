using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MicroIllustratorA8;

/// <summary>In-memory representation of a Micro Illustrator Atari 8-bit (.mia) image.</summary>
public sealed class MicroIllustratorA8File : IImageFileFormat<MicroIllustratorA8File> {

  /// <summary>Exact file size: 40 bytes/row x 192 rows.</summary>
  public const int ExpectedFileSize = 7680;

  /// <summary>Width in pixels (ANTIC Mode E, 160 pixels wide).</summary>
  internal const int PixelWidth = 160;

  /// <summary>Height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per row in the raw screen dump.</summary>
  internal const int BytesPerRow = 40;

  /// <summary>Bits per pixel (2bpp, 4 pixels per byte).</summary>
  internal const int BitsPerPixel = 2;

  static string IImageFileFormat<MicroIllustratorA8File>.PrimaryExtension => ".mia";
  static string[] IImageFileFormat<MicroIllustratorA8File>.FileExtensions => [".mia"];
  static FormatCapability IImageFileFormat<MicroIllustratorA8File>.Capabilities => FormatCapability.IndexedOnly;
  static MicroIllustratorA8File IImageFileFormat<MicroIllustratorA8File>.FromFile(FileInfo file) => MicroIllustratorA8Reader.FromFile(file);
  static MicroIllustratorA8File IImageFileFormat<MicroIllustratorA8File>.FromBytes(byte[] data) => MicroIllustratorA8Reader.FromBytes(data);
  static MicroIllustratorA8File IImageFileFormat<MicroIllustratorA8File>.FromStream(Stream stream) => MicroIllustratorA8Reader.FromStream(stream);
  static byte[] IImageFileFormat<MicroIllustratorA8File>.ToBytes(MicroIllustratorA8File file) => MicroIllustratorA8Writer.ToBytes(file);

  /// <summary>Always 160.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw 2bpp ANTIC Mode E screen data (7680 bytes, 4 pixels per byte).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Default 4-color ANTIC Mode E palette as RGB triplets.</summary>
  private static readonly byte[] _DefaultPalette = [
    0x00, 0x00, 0x00, // 0: black
    0x88, 0x44, 0x00, // 1: brown
    0x00, 0xAA, 0x44, // 2: green
    0xDD, 0xCC, 0x88, // 3: tan
  ];

  /// <summary>Converts this Micro Illustrator image to an Indexed8 raw image with a 4-entry palette.</summary>
  public static RawImage ToRawImage(MicroIllustratorA8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        var b = srcOffset < file.PixelData.Length ? file.PixelData[srcOffset] : (byte)0;

        for (var p = 0; p < 4; ++p) {
          var shift = (3 - p) * 2;
          var index = (b >> shift) & 0x03;
          var x = byteCol * 4 + p;
          if (x < PixelWidth)
            pixels[y * PixelWidth + x] = (byte)index;
        }
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _DefaultPalette[..],
      PaletteCount = 4,
    };
  }

  /// <summary>Not supported. Micro Illustrator images use a fixed ANTIC Mode E palette.</summary>
  public static MicroIllustratorA8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to MicroIllustratorA8File is not supported.");
  }
}
