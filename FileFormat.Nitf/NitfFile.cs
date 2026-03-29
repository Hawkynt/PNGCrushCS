using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Nitf;

/// <summary>Pixel representation mode for NITF images.</summary>
public enum NitfImageMode : byte {
  Grayscale,
  Rgb,
}

/// <summary>In-memory representation of a NITF (National Imagery Transmission Format) image.</summary>
[FormatMagicBytes([0x4E, 0x49, 0x54, 0x46])]
public sealed class NitfFile : IImageFileFormat<NitfFile> {

  static string IImageFileFormat<NitfFile>.PrimaryExtension => ".ntf";
  static string[] IImageFileFormat<NitfFile>.FileExtensions => [".ntf", ".nitf"];
  static NitfFile IImageFileFormat<NitfFile>.FromFile(FileInfo file) => NitfReader.FromFile(file);
  static NitfFile IImageFileFormat<NitfFile>.FromBytes(byte[] data) => NitfReader.FromBytes(data);
  static NitfFile IImageFileFormat<NitfFile>.FromStream(Stream stream) => NitfReader.FromStream(stream);
  static byte[] IImageFileFormat<NitfFile>.ToBytes(NitfFile file) => NitfWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Image representation mode (Grayscale or RGB).</summary>
  public NitfImageMode Mode { get; init; } = NitfImageMode.Rgb;

  /// <summary>File title from the NITF file header (up to 80 characters).</summary>
  public string Title { get; init; } = string.Empty;

  /// <summary>Security classification character (U/R/C/S/T).</summary>
  public char Classification { get; init; } = 'U';

  /// <summary>Raw pixel data bytes (Grayscale: 1 byte/pixel, RGB: 3 bytes/pixel band-sequential).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(NitfFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Mode switch {
      NitfImageMode.Grayscale => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      },
      NitfImageMode.Rgb => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = PixelConverter.BandSequentialToInterleaved(file.PixelData, file.Width * file.Height, 3),
      },
      _ => throw new ArgumentException($"Unsupported NITF image mode: {file.Mode}", nameof(file)),
    };
  }

  public static NitfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        Mode = NitfImageMode.Grayscale,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        Mode = NitfImageMode.Rgb,
        PixelData = PixelConverter.InterleavedToBandSequential(image.PixelData, image.Width * image.Height, 3),
      },
      _ => throw new ArgumentException($"Unsupported pixel format for NITF: {image.Format}", nameof(image)),
    };
  }

}
