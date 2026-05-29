using System;
using FileFormat.Core;

namespace FileFormat.LucasFilm;

/// <summary>In-memory representation of a LucasFilm LFF RGB image.</summary>
public readonly record struct LucasFilmFile : IImageFormatReader<LucasFilmFile>, IImageToRawImage<LucasFilmFile>, IImageFromRawImage<LucasFilmFile>, IImageFormatWriter<LucasFilmFile> {

  static string IImageFormatMetadata<LucasFilmFile>.PrimaryExtension => ".lff";
  static string[] IImageFormatMetadata<LucasFilmFile>.FileExtensions => [".lff"];
  static LucasFilmFile IImageFormatReader<LucasFilmFile>.FromSpan(ReadOnlySpan<byte> data) => LucasFilmReader.FromSpan(data);
  static byte[] IImageFormatWriter<LucasFilmFile>.ToBytes(LucasFilmFile file) => LucasFilmWriter.ToBytes(file);

  /// <summary>Magic bytes: "LFF\0" (0x4C 0x46 0x46 0x00).</summary>
  internal static readonly byte[] Magic = [0x4C, 0x46, 0x46, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + channels(2) + reserved(4) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (24 = RGB).</summary>
  public ushort Bpp { get; init; }

  /// <summary>Number of channels.</summary>
  public ushort Channels { get; init; }

  /// <summary>Reserved bytes.</summary>
  public uint Reserved { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(LucasFilmFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static LucasFilmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bpp = 24,
      Channels = 3,
      PixelData = image.PixelData[..],
    };
  }
}
