using System;
using FileFormat.FalconScreen;
using FileFormat.Core;

namespace FileFormat.SpeederFalcon;

/// <summary>In-memory representation of a Speeder Falcon true-color (.spf) screen dump.</summary>
public readonly record struct SpeederFalconFile : IImageFormatReader<SpeederFalconFile>, IImageToRawImage<SpeederFalconFile>, IImageFromRawImage<SpeederFalconFile>, IImageFormatWriter<SpeederFalconFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFormatMetadata<SpeederFalconFile>.PrimaryExtension => ".spf";
  static string[] IImageFormatMetadata<SpeederFalconFile>.FileExtensions => [".spf"];
  static SpeederFalconFile IImageFormatReader<SpeederFalconFile>.FromSpan(ReadOnlySpan<byte> data) => SpeederFalconReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<SpeederFalconFile>.VideoModes => [
    new("Default", [(320, 240)]),
  ];
  static byte[] IImageFormatWriter<SpeederFalconFile>.ToBytes(SpeederFalconFile file) => SpeederFalconWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SpeederFalconFile file) => FalconTrueColour.ToRawImage(file.PixelData);

  public static SpeederFalconFile FromRawImage(RawImage image) => new() { PixelData = FalconTrueColour.FromRawImage(image) };
}
