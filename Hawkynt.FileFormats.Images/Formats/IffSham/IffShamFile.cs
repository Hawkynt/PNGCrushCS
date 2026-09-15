using System;
using FileFormat.Core;
using FileFormat.Ilbm;

namespace FileFormat.IffSham;

/// <summary>In-memory representation of an IFF SHAM (Sliced HAM) image.</summary>
[VerifiedBy(ConformanceOracle.Recoil2Png, ConformanceOracle.FFmpeg, ConformanceOracle.IrfanView)]
public readonly record struct IffShamFile : IImageFormatReader<IffShamFile>, IImageToRawImage<IffShamFile>, IImageFromRawImage<IffShamFile>, IImageFormatWriter<IffShamFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Width of the original non-interlaced SHAM display.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Height of the original non-interlaced SHAM display.</summary>
  internal const int DefaultHeight = 200;

  /// <summary>SHAM uses the Amiga's six-plane HAM mode.</summary>
  internal const int NumPlanes = 6;

  /// <summary>Number of base colours supplied for each sliced palette.</summary>
  internal const int PaletteEntries = 16;

  /// <summary>RGB bytes occupied by one expanded scanline palette.</summary>
  internal const int PaletteBytesPerScanline = PaletteEntries * 3;

  /// <summary>Amiga viewport bit selecting Hold-And-Modify mode.</summary>
  internal const uint HamViewportMode = 0x0000_0800;

  static string IImageFormatMetadata<IffShamFile>.PrimaryExtension => ".sham";
  static string[] IImageFormatMetadata<IffShamFile>.FileExtensions => [".sham"];
  static IffShamFile IImageFormatReader<IffShamFile>.FromSpan(ReadOnlySpan<byte> data) => IffShamReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffShamFile>.ToBytes(IffShamFile file) => IffShamWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Original file bytes when the instance came from a reader. Kept for compatibility with the old
  /// raw-container model; newly authored files use <see cref="PixelData"/> and <see cref="ScanlinePalettes"/>.
  /// </summary>
  public byte[] RawData { get; init; }

  /// <summary>One six-bit HAM command per pixel, stored in the low six bits of each byte.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Expanded RGB palette data, sixteen RGB triplets per slice. A normal 320x200 SHAM image carries
  /// one slice per scanline; interlaced files found in the wild may carry one for each pair of lines.
  /// </summary>
  public byte[] ScanlinePalettes { get; init; }

  /// <summary>Decodes the sliced HAM commands to RGB pixels.</summary>
  public static RawImage ToRawImage(IffShamFile file) {
    ValidateDecoded(file, nameof(file));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = HamDecoder.Decode(file.PixelData, file.ScanlinePalettes, file.Width, file.Height, NumPlanes, PaletteEntries),
    };
  }

  /// <summary>
  /// Creates a canonical 320x200 SHAM image, choosing a sixteen-colour base palette independently
  /// for every scanline and then greedily selecting the nearest HAM6 command for every pixel.
  /// </summary>
  public static IffShamFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return IffShamEncoder.Encode(image);
  }

  internal static void ValidateDecoded(IffShamFile file, string parameterName) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("SHAM dimensions must be positive.", parameterName);

    var pixelCount = (long)file.Width * file.Height;
    if (pixelCount > int.MaxValue)
      throw new ArgumentException("SHAM dimensions are too large to address in memory.", parameterName);
    if (file.PixelData is null || file.PixelData.Length != (int)pixelCount)
      throw new ArgumentException($"SHAM pixel data must contain exactly {pixelCount} HAM commands.", parameterName);
    if (file.ScanlinePalettes is null || file.ScanlinePalettes.Length < PaletteBytesPerScanline || file.ScanlinePalettes.Length % PaletteBytesPerScanline != 0)
      throw new ArgumentException($"SHAM palette data must contain complete {PaletteEntries}-colour slices.", parameterName);

    foreach (var command in file.PixelData)
      if (command >= 1 << NumPlanes)
        throw new ArgumentException("SHAM pixel commands must fit in six bits.", parameterName);
  }

  internal static void ValidateForWriting(IffShamFile file, string parameterName) {
    ValidateDecoded(file, parameterName);

    if (file.Width != DefaultWidth || file.Height != DefaultHeight)
      throw new ArgumentException($"New SHAM files use the historical {DefaultWidth}x{DefaultHeight} non-interlaced display.", parameterName);
    if (file.ScanlinePalettes.Length != DefaultHeight * PaletteBytesPerScanline)
      throw new ArgumentException("A newly written SHAM file must carry one complete palette for every scanline.", parameterName);
  }
}
