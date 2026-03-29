using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Hdr;

/// <summary>In-memory representation of a Radiance HDR image.</summary>
[FormatMagicBytes([0x23, 0x3F])]
public sealed class HdrFile : IImageFileFormat<HdrFile> {

  static string IImageFileFormat<HdrFile>.PrimaryExtension => ".hdr";
  static string[] IImageFileFormat<HdrFile>.FileExtensions => [".hdr", ".rgbe", ".xyze", ".rad"];
  static HdrFile IImageFileFormat<HdrFile>.FromFile(FileInfo file) => HdrReader.FromFile(file);
  static HdrFile IImageFileFormat<HdrFile>.FromBytes(byte[] data) => HdrReader.FromBytes(data);
  static HdrFile IImageFileFormat<HdrFile>.FromStream(Stream stream) => HdrReader.FromStream(stream);
  static RawImage IImageFileFormat<HdrFile>.ToRawImage(HdrFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<HdrFile>.ToBytes(HdrFile file) => HdrWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public float Exposure { get; init; } = 1.0f;
  public float[] PixelData { get; init; } = [];

  /// <summary>Converts this HDR image to a 16-bit <see cref="RawImage"/> using Reinhard tone mapping with exposure.</summary>
  public RawImage ToRawImage() {
    var width = this.Width;
    var height = this.Height;
    var exposure = this.Exposure;
    var src = this.PixelData;
    var pixelCount = width * height;
    var result = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 3;
      var di = i * 6;
      for (var c = 0; c < 3; ++c) {
        var v = Math.Max(src[si + c] * exposure, 0f);
        var mapped = v / (1f + v);
        var u16 = (ushort)Math.Clamp(mapped * 65535f, 0, 65535);
        result[di + c * 2] = (byte)(u16 >> 8);
        result[di + c * 2 + 1] = (byte)u16;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb48,
      PixelData = result,
    };
  }

  /// <summary>Creates an <see cref="HdrFile"/> from a <see cref="RawImage"/>. Accepts Rgb48 (lossless) or any format convertible to Rgb48.</summary>
  public static HdrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb48 = PixelConverter.Convert(image, PixelFormat.Rgb48);
    var width = rgb48.Width;
    var height = rgb48.Height;
    var src = rgb48.PixelData;
    var pixelCount = width * height;
    var floats = new float[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 6;
      var di = i * 3;
      for (var c = 0; c < 3; ++c) {
        var u16 = (src[si + c * 2] << 8) | src[si + c * 2 + 1];
        floats[di + c] = u16 / 65535.0f;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Exposure = 1.0f,
      PixelData = floats,
    };
  }
}
