using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.C16Plus4;

/// <summary>In-memory representation of a Commodore 16/Plus4 multicolor screen image.</summary>
public sealed class C16Plus4File : IImageFileFormat<C16Plus4File> {

  internal const int FixedWidth = 160;
  internal const int FixedHeight = 200;
  internal const int FileSize = 10003;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFileFormat<C16Plus4File>.PrimaryExtension => ".c16";
  static string[] IImageFileFormat<C16Plus4File>.FileExtensions => [".c16", ".plus4"];
  static FormatCapability IImageFileFormat<C16Plus4File>.Capabilities => FormatCapability.IndexedOnly;
  static C16Plus4File IImageFileFormat<C16Plus4File>.FromFile(FileInfo file) => C16Plus4Reader.FromFile(file);
  static C16Plus4File IImageFileFormat<C16Plus4File>.FromBytes(byte[] data) => C16Plus4Reader.FromBytes(data);
  static C16Plus4File IImageFileFormat<C16Plus4File>.FromStream(Stream stream) => C16Plus4Reader.FromStream(stream);
  static byte[] IImageFileFormat<C16Plus4File>.ToBytes(C16Plus4File file) => C16Plus4Writer.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(C16Plus4File file) {
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

  public static C16Plus4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
