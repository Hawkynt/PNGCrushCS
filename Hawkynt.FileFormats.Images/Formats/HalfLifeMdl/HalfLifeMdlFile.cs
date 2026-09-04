using System;
using FileFormat.Core;

namespace FileFormat.HalfLifeMdl;

/// <summary>In-memory representation of a Half-Life Model texture image.</summary>
public readonly record struct HalfLifeMdlFile : IImageFormatReader<HalfLifeMdlFile>, IImageToRawImage<HalfLifeMdlFile>, IImageFromRawImage<HalfLifeMdlFile>, IImageFormatWriter<HalfLifeMdlFile> {

  internal const int HeaderSize = 16;

  static string IImageFormatMetadata<HalfLifeMdlFile>.PrimaryExtension => ".mdltex";
  static string[] IImageFormatMetadata<HalfLifeMdlFile>.FileExtensions => [".mdltex"];
  static HalfLifeMdlFile IImageFormatReader<HalfLifeMdlFile>.FromSpan(ReadOnlySpan<byte> data) => HalfLifeMdlReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<HalfLifeMdlFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])];
  static byte[] IImageFormatWriter<HalfLifeMdlFile>.ToBytes(HalfLifeMdlFile file) => HalfLifeMdlWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  /// <remarks>
  /// This model of the texture is header plus indices; the 768-byte colour table a texture carries
  /// inside a whole .mdl is not part of what gets extracted here, so the ramp from
  /// <see cref="IndexedPalette"/> stands in and the picture is at least drawable. Reading the real
  /// table needs a sample of the extracted layout to pin where it sits.
  /// </remarks>
  public static RawImage ToRawImage(HalfLifeMdlFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = IndexedPalette.GrayRamp(256),
      PaletteCount = 256,
    };
  }

  public static HalfLifeMdlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
