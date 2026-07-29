using System;
using FileFormat.Core;

namespace FileFormat.MonoStar;

/// <summary>In-memory representation of an Atari ST MonoSTar object (.obj).</summary>
/// <remarks>
/// A six-byte header giving the size — both dimensions stored one less than they are — and a
/// marker identifying the monochrome variant, followed by a one-bit-per-pixel bitmap whose rows
/// are padded to a whole number of 16-bit words. A set bit is ink on white paper.
/// <para>
/// The same extension also carries ColorSTar objects, which lead with sixteen palette entries
/// written as ASCII decimals and then four bitplanes; those are not written or read here.
/// </para>
/// </remarks>
public readonly record struct MonoStarFile
  : IImageFormatReader<MonoStarFile>, IImageToRawImage<MonoStarFile>,
    IImageFromRawImage<MonoStarFile>, IImageFormatWriter<MonoStarFile> {

  /// <summary>Size of the header.</summary>
  public const int HeaderSize = 6;

  /// <summary>The two bytes marking the monochrome variant.</summary>
  public static ReadOnlySpan<byte> MonochromeMarker => [0, 1];

  /// <summary>Colours a monochrome object shows.</summary>
  public const int ColorCount = 2;

  static string IImageFormatMetadata<MonoStarFile>.PrimaryExtension => ".obj";
  static string[] IImageFormatMetadata<MonoStarFile>.FileExtensions => [".obj"];
  static MonoStarFile IImageFormatReader<MonoStarFile>.FromSpan(ReadOnlySpan<byte> data) => MonoStarReader.FromSpan(data);
  static byte[] IImageFormatWriter<MonoStarFile>.ToBytes(MonoStarFile file) => MonoStarWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MonoStarFile>.VideoModes => [
    new("Monochrome object", [(IntegerRange.Any, IntegerRange.Any)], [ColorCount])
  ];

  /// <summary>Image width.</summary>
  public int Width { get; init; }

  /// <summary>Image height.</summary>
  public int Height { get; init; }

  /// <summary>The bitmap, one bit per pixel, rows padded to whole words.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Bytes per row: the bits rounded up to a whole number of 16-bit words.</summary>
  public static int StrideFor(int width) {
    var bytes = (width + 7) >> 3;

    return bytes + (bytes & 1);
  }

  /// <summary>Total file size for a given size.</summary>
  public static int FileSizeFor(int width, int height) => HeaderSize + StrideFor(width) * height;

  public static RawImage ToRawImage(MonoStarFile file) {
    var stride = StrideFor(file.Width);
    var data = file.BitmapData ?? [];
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var index = y * stride + (x >> 3);
      var bit = index < data.Length ? (data[index] >> (~x & 7)) & 1 : 0;
      pixels[y * file.Width + x] = (byte)bit;
    }

    // Index 0 is the paper the object is drawn on; a set bit is ink.
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = ColorCount,
    };
  }

  public static MonoStarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > 65536 || image.Height is < 1 or > 65536)
      throw new ArgumentException($"A MonoSTar object is between 1x1 and 65536x65536, got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var stride = StrideFor(image.Width);
    var data = new byte[stride * image.Height];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var pixel = (y * image.Width + x) * 4;
      // Ink is the dark end, so anything below mid-grey sets its bit.
      if (bgra.PixelData[pixel] + bgra.PixelData[pixel + 1] + bgra.PixelData[pixel + 2] < 384)
        data[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }

    return new() { Width = image.Width, Height = image.Height, BitmapData = data };
  }
}
