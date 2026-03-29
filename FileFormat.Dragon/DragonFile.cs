using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Dragon;

/// <summary>In-memory representation of a Dragon 32/64 PMODE 4 screen image.</summary>
public sealed class DragonFile : IImageFileFormat<DragonFile> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 192;
  internal const int FileSize = 6144;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<DragonFile>.PrimaryExtension => ".dgn";
  static string[] IImageFileFormat<DragonFile>.FileExtensions => [".dgn"];
  static FormatCapability IImageFileFormat<DragonFile>.Capabilities => FormatCapability.MonochromeOnly;
  static DragonFile IImageFileFormat<DragonFile>.FromFile(FileInfo file) => DragonReader.FromFile(file);
  static DragonFile IImageFileFormat<DragonFile>.FromBytes(byte[] data) => DragonReader.FromBytes(data);
  static DragonFile IImageFileFormat<DragonFile>.FromStream(Stream stream) => DragonReader.FromStream(stream);
  static byte[] IImageFileFormat<DragonFile>.ToBytes(DragonFile file) => DragonWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DragonFile file) {
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

  public static DragonFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
