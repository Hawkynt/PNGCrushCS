using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Gbr;

/// <summary>In-memory representation of a GIMP Brush (GBR) version 2 image.</summary>
[FormatMagicBytes([0x47, 0x49, 0x4D, 0x50], offset: 20)]
public readonly record struct GbrFile : IImageFormatReader<GbrFile>, IImageToRawImage<GbrFile>, IImageFromRawImage<GbrFile>, IImageFormatWriter<GbrFile> {

  static string IImageFormatMetadata<GbrFile>.PrimaryExtension => ".gbr";
  static string[] IImageFormatMetadata<GbrFile>.FileExtensions => [".gbr"];
  static GbrFile IImageFormatReader<GbrFile>.FromSpan(ReadOnlySpan<byte> data) => GbrReader.FromSpan(data);
  static byte[] IImageFormatWriter<GbrFile>.ToBytes(GbrFile file) => GbrWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bytes per pixel (1 = grayscale mask, 3 = RGB, 4 = RGBA).</summary>
  public int BytesPerPixel { get; init; }

  /// <summary>Brush spacing in percent.</summary>
  public int Spacing { get; init; }

  /// <summary>Brush name (UTF-8, stored null-terminated in file).</summary>
  public string Name { get; init; }

  /// <summary>Raw pixel data (width * height * bytes_per_pixel bytes, row-major).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GbrFile file) {
    // The depth is the byte count per pixel, so it names the layout outright: the mask alone, an RGB
    // brush with no mask (what XnView/nconvert writes), or colour plus mask.
    var format = file.BytesPerPixel switch {
      1 => PixelFormat.Gray8,
      3 => PixelFormat.Rgb24,
      4 => PixelFormat.Rgba32,
      var other => throw new InvalidDataException($"Invalid GBR bytes per pixel: {other} (expected 1, 3 or 4).")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static GbrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // We read depth 3, but we do not write it: GIMP refuses a brush that is not GRAY or RGBA, so an
    // RGB source gains an opaque mask here rather than becoming a file GIMP would reject. RGBA is
    // named first because anything not already in this list is converted to the first entry, and a
    // colour picture converted to Gray8 would lose its colour on the way to a format that holds it.
    image = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Gray8);
    if (image.Format is not (PixelFormat.Gray8 or PixelFormat.Rgba32))
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    var bpp = image.Format == PixelFormat.Gray8 ? 1 : 4;
    return new() {
      Width = image.Width,
      Height = image.Height,
      BytesPerPixel = bpp,
      Spacing = 10,
      Name = "Untitled",
      PixelData = image.PixelData[..],
    };
  }
}
