using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vic20;

/// <summary>In-memory representation of a Commodore VIC-20 screen dump image.</summary>
public sealed class Vic20File : IImageFileFormat<Vic20File> {

  internal const int FixedWidth = 176;
  internal const int FixedHeight = 184;
  internal const int FileSize = 4096;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFileFormat<Vic20File>.PrimaryExtension => ".vic20";
  static string[] IImageFileFormat<Vic20File>.FileExtensions => [".vic20", ".prg"];
  static FormatCapability IImageFileFormat<Vic20File>.Capabilities => FormatCapability.IndexedOnly;
  static Vic20File IImageFileFormat<Vic20File>.FromFile(FileInfo file) => Vic20Reader.FromFile(file);
  static Vic20File IImageFileFormat<Vic20File>.FromBytes(byte[] data) => Vic20Reader.FromBytes(data);
  static Vic20File IImageFileFormat<Vic20File>.FromStream(Stream stream) => Vic20Reader.FromStream(stream);
  static byte[] IImageFileFormat<Vic20File>.ToBytes(Vic20File file) => Vic20Writer.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Vic20File file) {
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

  public static Vic20File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
