using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GemImg;

/// <summary>In-memory representation of a GEM IMG raster image.</summary>
public sealed class GemImgFile : IImageFileFormat<GemImgFile> {

  static string IImageFileFormat<GemImgFile>.PrimaryExtension => ".img";
  static string[] IImageFileFormat<GemImgFile>.FileExtensions => [".img"];
  static FormatCapability IImageFileFormat<GemImgFile>.Capabilities => FormatCapability.IndexedOnly;
  static GemImgFile IImageFileFormat<GemImgFile>.FromFile(FileInfo file) => GemImgReader.FromFile(file);
  static GemImgFile IImageFileFormat<GemImgFile>.FromBytes(byte[] data) => GemImgReader.FromBytes(data);
  static GemImgFile IImageFileFormat<GemImgFile>.FromStream(Stream stream) => GemImgReader.FromStream(stream);
  static byte[] IImageFileFormat<GemImgFile>.ToBytes(GemImgFile file) => GemImgWriter.ToBytes(file);
  public int Version { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public int NumPlanes { get; init; }
  public int PatternLength { get; init; }
  public int PixelWidth { get; init; }
  public int PixelHeight { get; init; }
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this GEM IMG file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(GemImgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.NonInterleavedPlanarToChunky(file.PixelData, file.Width, file.Height, file.NumPlanes);
    var paletteCount = Math.Min(1 << file.NumPlanes, 256);
    var palette = new byte[paletteCount * 3];

    // GEM convention: index 0 = white, index 1 = black, remaining evenly spaced
    palette[0] = 255;
    palette[1] = 255;
    palette[2] = 255;
    if (paletteCount > 1) {
      palette[3] = 0;
      palette[4] = 0;
      palette[5] = 0;
    }

    for (var i = 2; i < paletteCount; ++i) {
      var gray = (byte)(255 - i * 255 / (paletteCount - 1));
      palette[i * 3] = gray;
      palette[i * 3 + 1] = gray;
      palette[i * 3 + 2] = gray;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates a <see cref="GemImgFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static GemImgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));

    var numPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(image.PaletteCount, 2))));
    var planar = PlanarConverter.ChunkyToNonInterleavedPlanar(image.PixelData, image.Width, image.Height, numPlanes);

    return new() {
      Version = 1,
      Width = image.Width,
      Height = image.Height,
      NumPlanes = numPlanes,
      PatternLength = 2,
      PixelWidth = 1,
      PixelHeight = 1,
      PixelData = planar,
    };
  }
}
