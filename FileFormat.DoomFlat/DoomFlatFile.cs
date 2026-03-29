using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DoomFlat;

/// <summary>In-memory representation of a Doom flat texture lump image.</summary>
public sealed class DoomFlatFile : IImageFileFormat<DoomFlatFile> {

  internal const int FixedWidth = 64;
  internal const int FixedHeight = 64;
  internal const int FileSize = 4096;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFileFormat<DoomFlatFile>.PrimaryExtension => ".flat";
  static string[] IImageFileFormat<DoomFlatFile>.FileExtensions => [".flat"];
  static FormatCapability IImageFileFormat<DoomFlatFile>.Capabilities => FormatCapability.IndexedOnly;
  static DoomFlatFile IImageFileFormat<DoomFlatFile>.FromFile(FileInfo file) => DoomFlatReader.FromFile(file);
  static DoomFlatFile IImageFileFormat<DoomFlatFile>.FromBytes(byte[] data) => DoomFlatReader.FromBytes(data);
  static DoomFlatFile IImageFileFormat<DoomFlatFile>.FromStream(Stream stream) => DoomFlatReader.FromStream(stream);
  static byte[] IImageFileFormat<DoomFlatFile>.ToBytes(DoomFlatFile file) => DoomFlatWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DoomFlatFile file) {
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

  public static DoomFlatFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
