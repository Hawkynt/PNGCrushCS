using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NokiaNlm;

/// <summary>In-memory representation of a Nokia Logo Manager image image.</summary>
public sealed class NokiaNlmFile : IImageFileFormat<NokiaNlmFile> {

  internal const int FixedWidth = 84;
  internal const int FixedHeight = 48;
  internal const int FileSize = 508;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<NokiaNlmFile>.PrimaryExtension => ".nlm";
  static string[] IImageFileFormat<NokiaNlmFile>.FileExtensions => [".nlm"];
  static FormatCapability IImageFileFormat<NokiaNlmFile>.Capabilities => FormatCapability.MonochromeOnly;
  static NokiaNlmFile IImageFileFormat<NokiaNlmFile>.FromFile(FileInfo file) => NokiaNlmReader.FromFile(file);
  static NokiaNlmFile IImageFileFormat<NokiaNlmFile>.FromBytes(byte[] data) => NokiaNlmReader.FromBytes(data);
  static NokiaNlmFile IImageFileFormat<NokiaNlmFile>.FromStream(Stream stream) => NokiaNlmReader.FromStream(stream);
  static byte[] IImageFileFormat<NokiaNlmFile>.ToBytes(NokiaNlmFile file) => NokiaNlmWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(NokiaNlmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static NokiaNlmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
