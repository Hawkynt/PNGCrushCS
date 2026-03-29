using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ScreenMaker;

/// <summary>In-memory representation of a Screen Maker image.</summary>
public sealed class ScreenMakerFile : IImageFileFormat<ScreenMakerFile> {

  static string IImageFileFormat<ScreenMakerFile>.PrimaryExtension => ".smk";
  static string[] IImageFileFormat<ScreenMakerFile>.FileExtensions => [".smk"];
  static FormatCapability IImageFileFormat<ScreenMakerFile>.Capabilities => FormatCapability.IndexedOnly;
  static ScreenMakerFile IImageFileFormat<ScreenMakerFile>.FromFile(FileInfo file) => ScreenMakerReader.FromFile(file);
  static ScreenMakerFile IImageFileFormat<ScreenMakerFile>.FromBytes(byte[] data) => ScreenMakerReader.FromBytes(data);
  static ScreenMakerFile IImageFileFormat<ScreenMakerFile>.FromStream(Stream stream) => ScreenMakerReader.FromStream(stream);
  static byte[] IImageFileFormat<ScreenMakerFile>.ToBytes(ScreenMakerFile file) => ScreenMakerWriter.ToBytes(file);

  /// <summary>Size of the header in bytes (2 width + 2 height).</summary>
  internal const int HeaderSize = 4;

  /// <summary>Size of the palette section in bytes (256 entries x 3 bytes RGB).</summary>
  internal const int PaletteDataSize = 768;

  /// <summary>Image width in pixels.</summary>
  public ushort Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public ushort Height { get; init; }

  /// <summary>Palette data (768 bytes, 256 entries x 3 bytes RGB).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Pixel data (width x height bytes, 1 byte per pixel, index into palette).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this Screen Maker image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ScreenMakerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var colorIndex = file.PixelData[i];
      var palOffset = colorIndex * 3;
      var outOffset = i * 3;
      rgb[outOffset] = file.Palette[palOffset];
      rgb[outOffset + 1] = file.Palette[palOffset + 1];
      rgb[outOffset + 2] = file.Palette[palOffset + 2];
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Screen Maker images require palette quantization.</summary>
  public static ScreenMakerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ScreenMakerFile is not supported.");
  }
}
