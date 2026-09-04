using System;
using FileFormat.Core;

namespace FileFormat.SiemensBmx;

/// <summary>In-memory representation of a Siemens mobile bitmap image.</summary>
public readonly record struct SiemensBmxFile : IImageFormatReader<SiemensBmxFile>, IImageToRawImage<SiemensBmxFile>, IImageFromRawImage<SiemensBmxFile>, IImageFormatWriter<SiemensBmxFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<SiemensBmxFile>.PrimaryExtension => ".bmx";
  static string[] IImageFormatMetadata<SiemensBmxFile>.FileExtensions => [".bmx"];
  static SiemensBmxFile IImageFormatReader<SiemensBmxFile>.FromSpan(ReadOnlySpan<byte> data) => SiemensBmxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SiemensBmxFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])];
  static byte[] IImageFormatWriter<SiemensBmxFile>.ToBytes(SiemensBmxFile file) => SiemensBmxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  /// <remarks>
  /// The BMX body is bare indices with no colour table behind them, so the ramp from
  /// <see cref="IndexedPalette"/> supplies one. Without it the picture came back indexed with a
  /// null palette, which no consumer can convert.
  /// </remarks>
  public static RawImage ToRawImage(SiemensBmxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = IndexedPalette.GrayRamp(256),
      PaletteCount = 256,
    };
  }

  public static SiemensBmxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
