using System;
using FileFormat.Core;

namespace FileFormat.Vips;

/// <summary>In-memory representation of a libvips native image (.v / .vips).</summary>
public readonly record struct VipsFile : IImageFormatReader<VipsFile>, IImageToRawImage<VipsFile>, IImageFromRawImage<VipsFile>, IImageFormatWriter<VipsFile> {

  static string IImageFormatMetadata<VipsFile>.PrimaryExtension => ".v";
  static string[] IImageFormatMetadata<VipsFile>.FileExtensions => [".v", ".vips"];
  static VipsFile IImageFormatReader<VipsFile>.FromSpan(ReadOnlySpan<byte> data) => VipsReader.FromSpan(data);
  static byte[] IImageFormatWriter<VipsFile>.ToBytes(VipsFile file) => VipsWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of bands/channels (1=gray, 3=RGB, 4=RGBA).</summary>
  public int Bands { get; init; }

  /// <summary>Band sample format.</summary>
  public VipsBandFormat BandFormat { get; init; }

  /// <summary>Raw pixel data (Bands bytes per pixel for UChar).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Whether the file was written in the other byte order, which its magic says.</summary>
  public bool IsSwapped { get; init; }

  public static RawImage ToRawImage(VipsFile file) {
    // A sixteen-bit band is ordinary rather than exotic: anything that has been through a scanner
    // or a colour-managed pipeline carries one. It is narrowed to the byte the magnitude lives in,
    // which depends on the order the file declares.
    if (file.BandFormat is VipsBandFormat.UShort or VipsBandFormat.Short) {
      var narrowed = _Narrow(file.PixelData ?? [], file.IsSwapped);
      file = file with { BandFormat = VipsBandFormat.UChar, PixelData = narrowed };
    }

    if (file.BandFormat != VipsBandFormat.UChar)
      throw new NotSupportedException($"Only UChar band format is supported, got {file.BandFormat}.");

    return file.Bands switch {
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      },
      3 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      },
      4 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = PixelConverter.Rgba32ToRgb24(file.PixelData, file.Width * file.Height),
      },
      _ => throw new NotSupportedException($"Unsupported band count: {file.Bands}."),
    };
  }

  public static VipsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        Bands = 1,
        BandFormat = VipsBandFormat.UChar,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        Bands = 3,
        BandFormat = VipsBandFormat.UChar,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image)),
    };
  }

  /// <summary>Narrows sixteen-bit samples to eight, keeping the byte that carries the magnitude.</summary>
  private static byte[] _Narrow(ReadOnlySpan<byte> data, bool isSwapped) {
    var narrowed = new byte[data.Length / 2];
    for (var i = 0; i < narrowed.Length; ++i)
      narrowed[i] = data[i * 2 + (isSwapped ? 0 : 1)];

    return narrowed;
  }
}
