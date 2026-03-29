using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Olpc565;

/// <summary>In-memory representation of an OLPC (One Laptop Per Child) RGB565 bitmap image.</summary>
public sealed class Olpc565File : IImageFileFormat<Olpc565File> {

  static string IImageFileFormat<Olpc565File>.PrimaryExtension => ".565";
  static string[] IImageFileFormat<Olpc565File>.FileExtensions => [".565"];
  static FormatCapability IImageFileFormat<Olpc565File>.Capabilities => FormatCapability.VariableResolution;
  static Olpc565File IImageFileFormat<Olpc565File>.FromFile(FileInfo file) => Olpc565Reader.FromFile(file);
  static Olpc565File IImageFileFormat<Olpc565File>.FromBytes(byte[] data) => Olpc565Reader.FromBytes(data);
  static Olpc565File IImageFileFormat<Olpc565File>.FromStream(Stream stream) => Olpc565Reader.FromStream(stream);
  static RawImage IImageFileFormat<Olpc565File>.ToRawImage(Olpc565File file) => file.ToRawImage();
  static byte[] IImageFileFormat<Olpc565File>.ToBytes(Olpc565File file) => Olpc565Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB565 pixel data (2 bytes per pixel, little-endian).</summary>
  public byte[] PixelData { get; init; } = [];

  public RawImage ToRawImage() {
    var pixelCount = this.Width * this.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var offset = i * 2;
      var pixel = (ushort)(this.PixelData[offset] | (this.PixelData[offset + 1] << 8));
      var r = (pixel >> 11) & 0x1F;
      var g = (pixel >> 5) & 0x3F;
      var b = pixel & 0x1F;

      rgb[i * 3] = (byte)(r * 255 / 31);
      rgb[i * 3 + 1] = (byte)(g * 255 / 63);
      rgb[i * 3 + 2] = (byte)(b * 255 / 31);
    }

    return new() {
      Width = this.Width,
      Height = this.Height,
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
