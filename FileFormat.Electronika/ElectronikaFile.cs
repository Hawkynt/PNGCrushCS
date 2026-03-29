using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Electronika;

/// <summary>In-memory representation of a Electronika BK screen dump image.</summary>
public sealed class ElectronikaFile : IImageFileFormat<ElectronikaFile> {

  internal const int FixedWidth = 512;
  internal const int FixedHeight = 256;
  internal const int FileSize = 16384;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<ElectronikaFile>.PrimaryExtension => ".bk";
  static string[] IImageFileFormat<ElectronikaFile>.FileExtensions => [".bk", ".ekr"];
  static FormatCapability IImageFileFormat<ElectronikaFile>.Capabilities => FormatCapability.MonochromeOnly;
  static ElectronikaFile IImageFileFormat<ElectronikaFile>.FromFile(FileInfo file) => ElectronikaReader.FromFile(file);
  static ElectronikaFile IImageFileFormat<ElectronikaFile>.FromBytes(byte[] data) => ElectronikaReader.FromBytes(data);
  static ElectronikaFile IImageFileFormat<ElectronikaFile>.FromStream(Stream stream) => ElectronikaReader.FromStream(stream);
  static byte[] IImageFileFormat<ElectronikaFile>.ToBytes(ElectronikaFile file) => ElectronikaWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(ElectronikaFile file) {
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

  public static ElectronikaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
