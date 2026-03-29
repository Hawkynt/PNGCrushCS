using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.C128;

/// <summary>In-memory representation of a Commodore 128 VDC screen image.</summary>
public sealed class C128File : IImageFileFormat<C128File> {

  internal const int FixedWidth = 640;
  internal const int FixedHeight = 200;
  internal const int FileSize = 16384;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<C128File>.PrimaryExtension => ".c128";
  static string[] IImageFileFormat<C128File>.FileExtensions => [".c128", ".vdc"];
  static FormatCapability IImageFileFormat<C128File>.Capabilities => FormatCapability.MonochromeOnly;
  static C128File IImageFileFormat<C128File>.FromFile(FileInfo file) => C128Reader.FromFile(file);
  static C128File IImageFileFormat<C128File>.FromBytes(byte[] data) => C128Reader.FromBytes(data);
  static C128File IImageFileFormat<C128File>.FromStream(Stream stream) => C128Reader.FromStream(stream);
  static byte[] IImageFileFormat<C128File>.ToBytes(C128File file) => C128Writer.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(C128File file) {
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

  public static C128File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
