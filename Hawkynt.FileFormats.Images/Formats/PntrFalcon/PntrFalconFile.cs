using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.PntrFalcon;

/// <summary>In-memory representation of a PntrFalcon (.pnf) screen dump.</summary>
public readonly record struct PntrFalconFile : IImageFormatReader<PntrFalconFile>, IImageToRawImage<PntrFalconFile>, IImageFromRawImage<PntrFalconFile>, IImageFormatWriter<PntrFalconFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<PntrFalconFile>.PrimaryExtension => ".pnf";
  static string[] IImageFormatMetadata<PntrFalconFile>.FileExtensions => [".pnf", ".pfl"];
  static PntrFalconFile IImageFormatReader<PntrFalconFile>.FromSpan(ReadOnlySpan<byte> data) => PntrFalconReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<PntrFalconFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<PntrFalconFile>.ToBytes(PntrFalconFile file) => PntrFalconWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PntrFalconFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static PntrFalconFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
