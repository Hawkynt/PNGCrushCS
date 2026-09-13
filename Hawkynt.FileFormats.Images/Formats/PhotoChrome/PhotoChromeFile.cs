using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.PhotoChrome;

/// <summary>In-memory representation of a PhotoChrome (.pcf) screen dump.</summary>
public readonly record struct PhotoChromeFile : IImageFormatReader<PhotoChromeFile>, IImageToRawImage<PhotoChromeFile>, IImageFromRawImage<PhotoChromeFile>, IImageFormatWriter<PhotoChromeFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<PhotoChromeFile>.PrimaryExtension => ".pcf";
  static string[] IImageFormatMetadata<PhotoChromeFile>.FileExtensions => [".pcf", ".phc"];
  static PhotoChromeFile IImageFormatReader<PhotoChromeFile>.FromSpan(ReadOnlySpan<byte> data) => PhotoChromeReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<PhotoChromeFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<PhotoChromeFile>.ToBytes(PhotoChromeFile file) => PhotoChromeWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PhotoChromeFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static PhotoChromeFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
