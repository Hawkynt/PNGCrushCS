using System;
using FileFormat.Core;
using FileFormat.FalconScreen;

namespace FileFormat.AtariFalcon;

/// <summary>In-memory representation of an Atari Falcon true-color (.ftc) screen dump.</summary>
[VerifiedBy(ConformanceOracle.Recoil2Png, ConformanceOracle.IrfanView)]
public readonly record struct AtariFalconFile : IImageFormatReader<AtariFalconFile>, IImageToRawImage<AtariFalconFile>, IImageFromRawImage<AtariFalconFile>, IImageFormatWriter<AtariFalconFile> {

  /// <summary>Pixels across. Not 320 — that is the size the other Falcon dump here holds.</summary>
  /// <remarks>
  /// This had 320 by 240, which is a real Falcon screen and a real format, but the one filed under
  /// a different extension. A picture of this format is wider, and a file of ours was 30720 bytes
  /// short of one.
  /// </remarks>
  public const int PixelWidth = 384;

  /// <summary>Rows.</summary>
  public const int PixelHeight = 240;

  /// <summary>The exact file size: two bytes a pixel, and nothing else in the file.</summary>
  public const int ExpectedFileSize = PixelWidth * PixelHeight * 2;

  static string IImageFormatMetadata<AtariFalconFile>.PrimaryExtension => ".ftc";
  static string[] IImageFormatMetadata<AtariFalconFile>.FileExtensions => [".ftc"];
  static AtariFalconFile IImageFormatReader<AtariFalconFile>.FromSpan(ReadOnlySpan<byte> data) => AtariFalconReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<AtariFalconFile>.VideoModes => [
    new("Default", [(PixelWidth, PixelHeight)]),
  ];
  static byte[] IImageFormatWriter<AtariFalconFile>.ToBytes(AtariFalconFile file) => AtariFalconWriter.ToBytes(file);

  /// <summary>Always 384.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 240.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AtariFalconFile file) => FalconTrueColour.ToRawImage(file.PixelData, PixelWidth, PixelHeight);

  public static AtariFalconFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image, PixelWidth, PixelHeight) };
}
