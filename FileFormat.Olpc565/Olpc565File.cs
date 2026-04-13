using System;
using FileFormat.Core;

namespace FileFormat.Olpc565;

/// <summary>In-memory representation of an OLPC (One Laptop Per Child) RGB565 bitmap image.</summary>
public sealed class Olpc565File :
  IImageFormatReader<Olpc565File>, IImageToRawImage<Olpc565File>,
  IImageFromRawImage<Olpc565File>, IImageFormatWriter<Olpc565File> {

  static string IImageFormatMetadata<Olpc565File>.PrimaryExtension => ".565";
  static string[] IImageFormatMetadata<Olpc565File>.FileExtensions => [".565"];
  static FormatCapability IImageFormatMetadata<Olpc565File>.Capabilities => FormatCapability.VariableResolution;
  static Olpc565File IImageFormatReader<Olpc565File>.FromSpan(ReadOnlySpan<byte> data) => Olpc565Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Olpc565File>.ToBytes(Olpc565File file) => Olpc565Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB565 pixel data (2 bytes per pixel, little-endian).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Olpc565File file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var offset = i * 2;
      var pixel = (ushort)(file.PixelData[offset] | (file.PixelData[offset + 1] << 8));
      var r = (pixel >> 11) & 0x1F;
      var g = (pixel >> 5) & 0x3F;
      var b = pixel & 0x1F;

      rgb[i * 3] = (byte)(r * 255 / 31);
      rgb[i * 3 + 1] = (byte)(g * 255 / 63);
      rgb[i * 3 + 2] = (byte)(b * 255 / 31);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static Olpc565File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var pixelCount = image.Width * image.Height;
    var pixelData = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var r = image.PixelData[i * 3];
      var g = image.PixelData[i * 3 + 1];
      var b = image.PixelData[i * 3 + 2];

      var r5 = (r * 31 + 127) / 255;
      var g6 = (g * 63 + 127) / 255;
      var b5 = (b * 31 + 127) / 255;

      var pixel = (ushort)((r5 << 11) | (g6 << 5) | b5);
      pixelData[i * 2] = (byte)(pixel & 0xFF);
      pixelData[i * 2 + 1] = (byte)(pixel >> 8);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixelData,
    };
  }
}
