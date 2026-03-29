using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Interlace8;

/// <summary>In-memory representation of an Atari Interlace Mode image (320x192, 2 frames combined).</summary>
public sealed class Interlace8File : IImageFileFormat<Interlace8File> {

  /// <summary>The size of one frame in bytes (40 bytes/line x 192 lines).</summary>
  public const int FrameSize = 7680;

  /// <summary>The exact file size: 2 frames x 7680 bytes.</summary>
  public const int ExpectedFileSize = FrameSize * 2;

  /// <summary>The fixed width in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height in pixels.</summary>
  public const int FixedHeight = 192;

  /// <summary>Bytes per scanline per frame (320 pixels / 8 bits per pixel = 40).</summary>
  internal const int BytesPerRow = 40;

  static string IImageFileFormat<Interlace8File>.PrimaryExtension => ".int8";
  static string[] IImageFileFormat<Interlace8File>.FileExtensions => [".int8"];
  static Interlace8File IImageFileFormat<Interlace8File>.FromFile(FileInfo file) => Interlace8Reader.FromFile(file);
  static Interlace8File IImageFileFormat<Interlace8File>.FromBytes(byte[] data) => Interlace8Reader.FromBytes(data);
  static Interlace8File IImageFileFormat<Interlace8File>.FromStream(Stream stream) => Interlace8Reader.FromStream(stream);
  static byte[] IImageFileFormat<Interlace8File>.ToBytes(Interlace8File file) => Interlace8Writer.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>First frame pixel data (7680 bytes, 1bpp, 40 bytes per row, 192 rows).</summary>
  public byte[] Frame1Data { get; init; } = [];

  /// <summary>Second frame pixel data (7680 bytes, 1bpp, 40 bytes per row, 192 rows).</summary>
  public byte[] Frame2Data { get; init; } = [];

  /// <summary>Converts this interlace image to a platform-independent <see cref="RawImage"/> in Gray8 format.</summary>
  /// <remarks>
  /// Both frames on = white (255), frame 1 only = light gray (170), frame 2 only = dark gray (85), neither = black (0).
  /// </remarks>
  public static RawImage ToRawImage(Interlace8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var gray = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var byteIndex = y * BytesPerRow + x / 8;
        var bitPosition = 7 - (x % 8);
        var bit1 = (file.Frame1Data[byteIndex] >> bitPosition) & 1;
        var bit2 = (file.Frame2Data[byteIndex] >> bitPosition) & 1;

        var value = (bit1, bit2) switch {
          (1, 1) => (byte)255,
          (1, 0) => (byte)170,
          (0, 1) => (byte)85,
          _ => (byte)0,
        };

        gray[y * FixedWidth + x] = value;
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Gray8,
      PixelData = gray,
    };
  }

  /// <summary>Not supported. Interlace images require two distinct frames that cannot be reconstructed from a single grayscale image without ambiguity.</summary>
  public static Interlace8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to Interlace8File is not supported.");
  }
}
