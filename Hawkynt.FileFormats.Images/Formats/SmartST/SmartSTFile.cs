using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.SmartST;

/// <summary>In-memory representation of a Smart ST true-color (.sst) screen dump.</summary>
public readonly record struct SmartSTFile : IImageFormatReader<SmartSTFile>, IImageToRawImage<SmartSTFile>, IImageFromRawImage<SmartSTFile>, IImageFormatWriter<SmartSTFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<SmartSTFile>.PrimaryExtension => ".sst";
  static string[] IImageFormatMetadata<SmartSTFile>.FileExtensions => [".sst", ".sst2"];
  static SmartSTFile IImageFormatReader<SmartSTFile>.FromSpan(ReadOnlySpan<byte> data) => SmartSTReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<SmartSTFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<SmartSTFile>.ToBytes(SmartSTFile file) => SmartSTWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SmartSTFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static SmartSTFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
