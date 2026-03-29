using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AnimPainter;

/// <summary>Per-frame data for an Anim Painter multicolor animation frame.</summary>
/// <param name="BitmapData">Bitmap data (8000 bytes).</param>
/// <param name="VideoMatrix">Screen RAM / video matrix (1000 bytes).</param>
/// <param name="ColorRam">Color RAM (1000 bytes).</param>
/// <param name="BackgroundColor">Background color index (0-15).</param>
public readonly record struct AnimPainterFrame(
  byte[] BitmapData,
  byte[] VideoMatrix,
  byte[] ColorRam,
  byte BackgroundColor
);

/// <summary>In-memory representation of an Anim Painter (animated C64 multicolor) image.</summary>
public sealed class AnimPainterFile : IImageFileFormat<AnimPainterFile> {

  static string IImageFileFormat<AnimPainterFile>.PrimaryExtension => ".anp";
  static string[] IImageFileFormat<AnimPainterFile>.FileExtensions => [".anp"];
  static AnimPainterFile IImageFileFormat<AnimPainterFile>.FromFile(FileInfo file) => AnimPainterReader.FromFile(file);
  static AnimPainterFile IImageFileFormat<AnimPainterFile>.FromBytes(byte[] data) => AnimPainterReader.FromBytes(data);
  static AnimPainterFile IImageFileFormat<AnimPainterFile>.FromStream(Stream stream) => AnimPainterReader.FromStream(stream);
  static byte[] IImageFileFormat<AnimPainterFile>.ToBytes(AnimPainterFile file) => AnimPainterWriter.ToBytes(file);

  /// <summary>Image width in pixels (multicolor).</summary>
  public const int ImageWidth = 160;

  /// <summary>Image height in pixels.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the bitmap data section in bytes per frame.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix (screen RAM) section in bytes per frame.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes per frame.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the background color field per frame.</summary>
  internal const int BackgroundColorSize = 1;

  /// <summary>Total bytes per frame: 8000 + 1000 + 1000 + 1 = 10001.</summary>
  internal const int BytesPerFrame = BitmapDataSize + VideoMatrixSize + ColorRamSize + BackgroundColorSize;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Animation frames.</summary>
  public IReadOnlyList<AnimPainterFrame> Frames { get; init; } = [];

  /// <summary>Number of animation frames.</summary>
  public int FrameCount => Frames.Count;

  /// <summary>Converts this Anim Painter image to a platform-independent <see cref="RawImage"/> in Rgb24 format by decoding the first frame as multicolor.</summary>
  public static RawImage ToRawImage(AnimPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new InvalidDataException("Anim Painter file contains no frames.");

    var frame = file.Frames[0];
    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y)
      for (var x = 0; x < ImageWidth; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = frame.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => frame.BackgroundColor & 0x0F,
          1 => (frame.VideoMatrix[cellIndex] >> 4) & 0x0F,
          2 => frame.VideoMatrix[cellIndex] & 0x0F,
          3 => frame.ColorRam[cellIndex] & 0x0F,
          _ => 0
        };

        var color = _C64Palette[colorIndex];
        var offset = (y * ImageWidth + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Anim Painter is a read-only format.</summary>
  public static AnimPainterFile FromRawImage(RawImage image)
    => throw new NotSupportedException("Creating Anim Painter files from raw images is not supported.");
}
