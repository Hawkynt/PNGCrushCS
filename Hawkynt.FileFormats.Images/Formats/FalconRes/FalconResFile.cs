using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.FalconRes;

/// <summary>In-memory representation of a Falcon Res (.frs) screen dump.</summary>
public readonly record struct FalconResFile : IImageFormatReader<FalconResFile>, IImageToRawImage<FalconResFile>, IImageFromRawImage<FalconResFile>, IImageFormatWriter<FalconResFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<FalconResFile>.PrimaryExtension => ".frs";
  static string[] IImageFormatMetadata<FalconResFile>.FileExtensions => [".frs"];
  static FalconResFile IImageFormatReader<FalconResFile>.FromSpan(ReadOnlySpan<byte> data) => FalconResReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<FalconResFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<FalconResFile>.ToBytes(FalconResFile file) => FalconResWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FalconResFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static FalconResFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
