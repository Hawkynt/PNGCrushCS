using System;
using FileFormat.Core;

namespace FileFormat.ScreenMaker;

/// <summary>In-memory representation of a Screen Maker image.</summary>
public readonly record struct ScreenMakerFile : IImageFormatReader<ScreenMakerFile>, IImageToRawImage<ScreenMakerFile>, IImageFromRawImage<ScreenMakerFile>, IImageFormatWriter<ScreenMakerFile> {

  static string IImageFormatMetadata<ScreenMakerFile>.PrimaryExtension => ".smk";
  static string[] IImageFormatMetadata<ScreenMakerFile>.FileExtensions => [".smk"];
  static ScreenMakerFile IImageFormatReader<ScreenMakerFile>.FromSpan(ReadOnlySpan<byte> data) => ScreenMakerReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ScreenMakerFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])];
  static byte[] IImageFormatWriter<ScreenMakerFile>.ToBytes(ScreenMakerFile file) => ScreenMakerWriter.ToBytes(file);

  /// <summary>Size of the header in bytes (2 width + 2 height).</summary>
  internal const int HeaderSize = 4;

  /// <summary>Size of the palette section in bytes (256 entries x 3 bytes RGB).</summary>
  internal const int PaletteDataSize = 768;

  /// <summary>Image width in pixels.</summary>
  public ushort Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public ushort Height { get; init; }

  /// <summary>Palette data (768 bytes, 256 entries x 3 bytes RGB).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixel data (width x height bytes, 1 byte per pixel, index into palette).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Screen Maker image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ScreenMakerFile file) {

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var colorIndex = file.PixelData[i];
      var palOffset = colorIndex * 3;
      var outOffset = i * 3;
      rgb[outOffset] = file.Palette[palOffset];
      rgb[outOffset + 1] = file.Palette[palOffset + 1];
      rgb[outOffset + 2] = file.Palette[palOffset + 2];
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }


  /// <summary>Encodes a picture as a Screen Maker file.</summary>
  /// <remarks>
  /// One of the few here with no fixed screen: the file states its own size, so the picture keeps
  /// the one it came with and is only brought inside what two bytes can express.
  /// </remarks>
  public static ScreenMakerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Clamp(image.Width, 1, ushort.MaxValue);
    var height = Math.Clamp(image.Height, 1, ushort.MaxValue);
    var bgra = PixelConverter.Convert(image.SampleTo(width, height), PixelFormat.Bgra32);
    var quantised = ColorQuantizer.Quantize(bgra.PixelData, width * height, 256);

    var palette = new byte[PaletteDataSize];
    quantised.Palette.AsSpan(0, Math.Min(quantised.Palette.Length, PaletteDataSize)).CopyTo(palette);

    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)quantised.Indices[i];

    return new() { Width = (ushort)width, Height = (ushort)height, Palette = palette, PixelData = pixels };
  }

}
