using System;
using FileFormat.Core;

namespace FileFormat.DrHalo;

/// <summary>In-memory representation of a Dr. Halo CUT image.</summary>
public readonly record struct DrHaloFile : IImageFormatReader<DrHaloFile>, IImageToRawImage<DrHaloFile>, IImageFromRawImage<DrHaloFile>, IImageFormatWriter<DrHaloFile> {

  static string IImageFormatMetadata<DrHaloFile>.PrimaryExtension => ".cut";
  static string[] IImageFormatMetadata<DrHaloFile>.FileExtensions => [".cut"];
  static DrHaloFile IImageFormatReader<DrHaloFile>.FromSpan(ReadOnlySpan<byte> data) => DrHaloReader.FromSpan(data);
  static byte[] IImageFormatWriter<DrHaloFile>.ToBytes(DrHaloFile file) => DrHaloWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }

  /// <remarks>
  /// Dr. Halo keeps its colours in a separate .PAL that the .CUT does not name, so a CUT read on its
  /// own has none. It gets the ramp from <see cref="IndexedPalette"/> rather than a null palette,
  /// which is the difference between a grey picture and an exception on every conversion.
  /// </remarks>
  public static RawImage ToRawImage(DrHaloFile file) {
    var palette = file.Palette is { Length: > 0 } p ? p[..] : IndexedPalette.GrayRamp(256);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = palette.Length / 3,
    };
  }

  public static DrHaloFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette is { } p ? p[..] : null,
    };
  }
}
