using System;
using FileFormat.Core;

namespace FileFormat.HiresFliCrest;

/// <summary>In-memory representation of a Hires FLI Designer (.hfc) picture for the Commodore 64.</summary>
/// <remarks>
/// A high-resolution FLI screen: one bit a pixel choosing between the two colours the video matrix
/// names, and eight matrices so every raster line of a cell names its own pair. The bitmap comes
/// first here — given a whole eight kilobytes rather than the eight thousand it uses — and the eight
/// matrices follow it a page apart, which is the granularity of the VIC-II's matrix pointer.
/// <para/>
/// It shows 112 rows and 296 columns. The rows are what the display routine has raster time for; the
/// columns are the row less the three cells drawn before the switch that makes FLI work can happen.
/// The old model said 320 by 200 and packed the matrices a thousand apart, which is neither.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct HiresFliCrestFile
  : IImageFormatReader<HiresFliCrestFile>, IImageToRawImage<HiresFliCrestFile>,
    IImageFromRawImage<HiresFliCrestFile>, IImageFormatWriter<HiresFliCrestFile> {

  static string IImageFormatMetadata<HiresFliCrestFile>.PrimaryExtension => ".hfc";
  static string[] IImageFormatMetadata<HiresFliCrestFile>.FileExtensions => [".hfc", ".hfd"];
  static HiresFliCrestFile IImageFormatReader<HiresFliCrestFile>.FromSpan(ReadOnlySpan<byte> data) => HiresFliCrestReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiresFliCrestFile>.ToBytes(HiresFliCrestFile file) => HiresFliCrestWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HiresFliCrestFile>.VideoModes => [
    new("Hires FLI Designer", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows the display routine has time for.</summary>
  public const int FixedHeight = 112;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the bitmap starts: straight after the load address.</summary>
  internal const int BitmapOffset = LoadAddressSize;

  /// <summary>The bitmap is given a whole eight kilobytes, not the eight thousand it fills.</summary>
  internal const int BitmapAreaSize = 8192;

  /// <summary>Where the video matrices start: after the bitmap's eight kilobytes.</summary>
  internal const int MatricesOffset = BitmapOffset + BitmapAreaSize;

  /// <summary>The length of a whole picture, which is also what identifies it.</summary>
  public const int FileSize = MatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>Default load address, which puts the bitmap at the foot of the bank at $4000.</summary>
  internal const ushort DefaultLoadAddress = 0x4000;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 112.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The bitmap, a cell at a time, in the eight kilobytes the format gives it.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(HiresFliCrestFile file)
    => Commodore64Fli.DecodeHires(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride, FixedHeight);

  /// <summary>Encodes a picture as a Hires FLI screen, scaling it to 296x112 first.</summary>
  public static HiresFliCrestFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[BitmapAreaSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    Commodore64Fli.EncodeHires(image, FixedHeight, 0, bitmap, matrices, Commodore64Fli.MatrixStride);

    return new() { LoadAddress = DefaultLoadAddress, BitmapData = bitmap, Matrices = matrices };
  }
}
