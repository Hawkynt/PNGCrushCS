using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CompW;

/// <summary>In-memory representation of a CompW WLM indexed image.</summary>
public sealed class CompWFile : IImageFileFormat<CompWFile> {

  static string IImageFileFormat<CompWFile>.PrimaryExtension => ".wlm";
  static string[] IImageFileFormat<CompWFile>.FileExtensions => [".wlm"];
  static FormatCapability IImageFileFormat<CompWFile>.Capabilities => FormatCapability.IndexedOnly;
  static CompWFile IImageFileFormat<CompWFile>.FromFile(FileInfo file) => CompWReader.FromFile(file);
  static CompWFile IImageFileFormat<CompWFile>.FromBytes(byte[] data) => CompWReader.FromBytes(data);
  static CompWFile IImageFileFormat<CompWFile>.FromStream(Stream stream) => CompWReader.FromStream(stream);
  static byte[] IImageFileFormat<CompWFile>.ToBytes(CompWFile file) => CompWWriter.ToBytes(file);

  /// <summary>Magic bytes: "CW" (0x43 0x57).</summary>
  internal static readonly byte[] Magic = [0x43, 0x57];

  /// <summary>Header size: magic(2) + width(2) + height(2) + bpp(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Palette size: 256 RGB triplets = 768 bytes.</summary>
  internal const int PaletteSize = 768;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (always 8).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>8-bit indexed pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Palette (768 bytes, 256 RGB triplets).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Converts this CompW image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CompWFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[file.Width * file.Height * 3];
    for (var i = 0; i < file.PixelData.Length; ++i) {
      var index = file.PixelData[i];
      var palOffset = index * 3;
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

  /// <summary>Not supported.</summary>
  public static CompWFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to CompWFile is not supported.");
  }
}
