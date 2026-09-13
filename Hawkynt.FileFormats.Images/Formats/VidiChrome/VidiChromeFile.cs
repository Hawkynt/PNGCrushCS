using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.VidiChrome;

/// <summary>In-memory representation of a VidiChrome true-color (.vdc) screen dump.</summary>
public readonly record struct VidiChromeFile : IImageFormatReader<VidiChromeFile>, IImageToRawImage<VidiChromeFile>, IImageFromRawImage<VidiChromeFile>, IImageFormatWriter<VidiChromeFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<VidiChromeFile>.PrimaryExtension => ".vdc";
  static string[] IImageFormatMetadata<VidiChromeFile>.FileExtensions => [".vdc", ".vdc2"];
  static VidiChromeFile IImageFormatReader<VidiChromeFile>.FromSpan(ReadOnlySpan<byte> data) => VidiChromeReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<VidiChromeFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<VidiChromeFile>.ToBytes(VidiChromeFile file) => VidiChromeWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(VidiChromeFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static VidiChromeFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
