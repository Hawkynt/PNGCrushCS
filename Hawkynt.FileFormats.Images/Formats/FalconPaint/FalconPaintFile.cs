using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.FalconPaint;

/// <summary>In-memory representation of a Falcon Paint (.fpn) screen dump.</summary>
public readonly record struct FalconPaintFile : IImageFormatReader<FalconPaintFile>, IImageToRawImage<FalconPaintFile>, IImageFromRawImage<FalconPaintFile>, IImageFormatWriter<FalconPaintFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<FalconPaintFile>.PrimaryExtension => ".fpn";
  static string[] IImageFormatMetadata<FalconPaintFile>.FileExtensions => [".fpn"];
  static FalconPaintFile IImageFormatReader<FalconPaintFile>.FromSpan(ReadOnlySpan<byte> data) => FalconPaintReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<FalconPaintFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<FalconPaintFile>.ToBytes(FalconPaintFile file) => FalconPaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FalconPaintFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static FalconPaintFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
