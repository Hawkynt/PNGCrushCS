using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.TriPaint;

/// <summary>In-memory representation of a TriPaint true-color (.tpf) screen dump.</summary>
public readonly record struct TriPaintFile : IImageFormatReader<TriPaintFile>, IImageToRawImage<TriPaintFile>, IImageFromRawImage<TriPaintFile>, IImageFormatWriter<TriPaintFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<TriPaintFile>.PrimaryExtension => ".tpf";
  static string[] IImageFormatMetadata<TriPaintFile>.FileExtensions => [".tpf"];
  static TriPaintFile IImageFormatReader<TriPaintFile>.FromSpan(ReadOnlySpan<byte> data) => TriPaintReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<TriPaintFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<TriPaintFile>.ToBytes(TriPaintFile file) => TriPaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(TriPaintFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static TriPaintFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
