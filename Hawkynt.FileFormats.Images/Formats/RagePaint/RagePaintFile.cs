using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.RagePaint;

/// <summary>In-memory representation of a Rage Paint true-color (.rge) screen dump.</summary>
public readonly record struct RagePaintFile : IImageFormatReader<RagePaintFile>, IImageToRawImage<RagePaintFile>, IImageFromRawImage<RagePaintFile>, IImageFormatWriter<RagePaintFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<RagePaintFile>.PrimaryExtension => ".rge";
  static string[] IImageFormatMetadata<RagePaintFile>.FileExtensions => [".rge"];
  static RagePaintFile IImageFormatReader<RagePaintFile>.FromSpan(ReadOnlySpan<byte> data) => RagePaintReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<RagePaintFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<RagePaintFile>.ToBytes(RagePaintFile file) => RagePaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(RagePaintFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static RagePaintFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
