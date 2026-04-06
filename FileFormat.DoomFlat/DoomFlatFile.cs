using System;
using FileFormat.Core;

namespace FileFormat.DoomFlat;

/// <summary>In-memory representation of a Doom flat texture lump image.</summary>
public readonly record struct DoomFlatFile : IImageFormatReader<DoomFlatFile>, IImageToRawImage<DoomFlatFile>, IImageFromRawImage<DoomFlatFile>, IImageFormatWriter<DoomFlatFile> {

  internal const int FixedWidth = 64;
  internal const int FixedHeight = 64;
  internal const int FileSize = 4096;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFormatMetadata<DoomFlatFile>.PrimaryExtension => ".flat";
  static string[] IImageFormatMetadata<DoomFlatFile>.FileExtensions => [".flat"];
  static DoomFlatFile IImageFormatReader<DoomFlatFile>.FromSpan(ReadOnlySpan<byte> data) => DoomFlatReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<DoomFlatFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<DoomFlatFile>.ToBytes(DoomFlatFile file) => DoomFlatWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DoomFlatFile file) {
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
