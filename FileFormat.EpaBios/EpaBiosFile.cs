using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EpaBios;

/// <summary>In-memory representation of a Award BIOS Logo (.epa) image.</summary>
public sealed class EpaBiosFile : IImageFileFormat<EpaBiosFile> {

  internal const int FixedWidth = 136;
  internal const int FixedHeight = 84;
  internal const int FileSize = 714;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFileFormat<EpaBiosFile>.PrimaryExtension => ".epa";
  static string[] IImageFileFormat<EpaBiosFile>.FileExtensions => [".epa"];
  static FormatCapability IImageFileFormat<EpaBiosFile>.Capabilities => FormatCapability.IndexedOnly;
  static EpaBiosFile IImageFileFormat<EpaBiosFile>.FromFile(FileInfo file) => EpaBiosReader.FromFile(file);
  static EpaBiosFile IImageFileFormat<EpaBiosFile>.FromBytes(byte[] data) => EpaBiosReader.FromBytes(data);
  static EpaBiosFile IImageFileFormat<EpaBiosFile>.FromStream(Stream stream) => EpaBiosReader.FromStream(stream);
  static byte[] IImageFileFormat<EpaBiosFile>.ToBytes(EpaBiosFile file) => EpaBiosWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(EpaBiosFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = _DefaultPalette[..],
      PaletteCount = 16,
    };
  }

  public static EpaBiosFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
