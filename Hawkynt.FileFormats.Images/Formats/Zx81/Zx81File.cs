using System;
using FileFormat.Core;

namespace FileFormat.Zx81;

/// <summary>In-memory representation of a Sinclair ZX81 display file image.</summary>
public readonly record struct Zx81File : IImageFormatReader<Zx81File>, IImageToRawImage<Zx81File>, IImageFromRawImage<Zx81File>, IImageFormatWriter<Zx81File> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 192;
  internal const int FileSize = 793;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<Zx81File>.PrimaryExtension => ".zx81";
  static string[] IImageFormatMetadata<Zx81File>.FileExtensions => [".zx81", ".p81"];
  static Zx81File IImageFormatReader<Zx81File>.FromSpan(ReadOnlySpan<byte> data) => Zx81Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Zx81File>.VideoModes => [new("Default", [(256, 192)], [2])];
  static byte[] IImageFormatWriter<Zx81File>.ToBytes(Zx81File file) => Zx81Writer.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Zx81File file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = [.._BlackWhitePalette],
      PaletteCount = 2,
    };
  }

  public static Zx81File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
