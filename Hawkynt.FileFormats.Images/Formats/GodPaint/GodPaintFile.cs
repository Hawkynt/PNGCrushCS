using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.GodPaint;

/// <summary>In-memory representation of a GodPaint (.gpn) screen dump.</summary>
[VerifiedBy(ConformanceOracle.Recoil2Png, ConformanceOracle.IrfanView)]
public readonly record struct GodPaintFile : IImageFormatReader<GodPaintFile>, IImageToRawImage<GodPaintFile>, IImageFromRawImage<GodPaintFile>, IImageFormatWriter<GodPaintFile> {

  /// <summary>Header size: two reserved bytes, then big-endian width and height.</summary>
  public const int HeaderSize = 6;

  /// <summary>Offset of the big-endian width/height pair.</summary>
  public const int DimensionsOffset = 2;

  /// <summary>Pixel data size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int PixelDataSize = 320 * 240 * 2;

  /// <summary>The exact file size.</summary>
  public const int ExpectedFileSize = HeaderSize + PixelDataSize;

  static string IImageFormatMetadata<GodPaintFile>.PrimaryExtension => ".gpn";
  static string[] IImageFormatMetadata<GodPaintFile>.FileExtensions => [".gpn", ".gdp", ".god"];
  static GodPaintFile IImageFormatReader<GodPaintFile>.FromSpan(ReadOnlySpan<byte> data) => GodPaintReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<GodPaintFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<GodPaintFile>.ToBytes(GodPaintFile file) => GodPaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GodPaintFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static GodPaintFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
