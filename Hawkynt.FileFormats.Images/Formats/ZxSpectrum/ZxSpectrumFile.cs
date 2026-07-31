using System;
using FileFormat.Core;

namespace FileFormat.ZxSpectrum;

/// <summary>In-memory representation of a ZX Spectrum screen (6912 bytes: 6144 bitmap + 768 attributes).</summary>
public readonly record struct ZxSpectrumFile : IImageFormatReader<ZxSpectrumFile>, IImageToRawImage<ZxSpectrumFile>, IImageFromRawImage<ZxSpectrumFile>, IImageFormatWriter<ZxSpectrumFile> {

  static string IImageFormatMetadata<ZxSpectrumFile>.PrimaryExtension => ".scr";
  static string[] IImageFormatMetadata<ZxSpectrumFile>.FileExtensions => [".scr", ".$s", ".$c", ".!s"];
  static ZxSpectrumFile IImageFormatReader<ZxSpectrumFile>.FromSpan(ReadOnlySpan<byte> data) => ZxSpectrumReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxSpectrumFile>.ToBytes(ZxSpectrumFile file) => ZxSpectrumWriter.ToBytes(file);

  /// <summary>ZX Spectrum normal palette (bright=0): Black, Blue, Red, Magenta, Green, Cyan, Yellow, White.</summary>
  private static readonly int[] _NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  private static readonly int[] _BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order (deinterleaved).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>768 bytes of attribute data, one per 8x8 cell (bit 7=flash, bit 6=bright, bits 5-3=paper, bits 2-0=ink).</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>Border color (0-7), not stored in the file data.</summary>
  public byte BorderColor { get; init; }

  /// <summary>Converts this ZX Spectrum screen to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ZxSpectrumFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var cellY = y / 8;
        var attribute = file.AttributeData[cellY * 32 + cellX];
        var bright = (attribute >> 6) & 1;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        var palette = bright == 1 ? _BrightPalette : _NormalPalette;
        var color = palette[bitValue == 1 ? ink : paper];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds a screen, choosing two colours for every eight-by-eight cell.</summary>
  /// <remarks>
  /// A Spectrum cell shows one ink and one paper and nothing else, so the whole of the encoding is
  /// which pair. Every one of the 128 available pairs is tried against the cell's sixty-four pixels
  /// and the cheapest kept — exact, and cheaper than any cleverness at that size.
  /// <para/>
  /// No error is diffused across a cell boundary. Spreading it would push a colour into a cell that
  /// cannot show it, and the attribute clash that follows looks worse than the banding it fixes,
  /// which is why Spectrum artists dithered inside a cell and never across one.
  /// </remarks>
  public static ZxSpectrumFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int width = 256, height = 192, columns = width / 8;
    var rgb = image.SampleTo(width, height);

    var bitmap = new byte[width * height / 8];
    var attributes = new byte[columns * (height / 8)];
    Span<byte> bits = stackalloc byte[8];

    for (var top = 0; top < height; top += 8)
    for (var left = 0; left < width; left += 8) {
      attributes[top / 8 * columns + left / 8] =
        ZxSpectrumGraphics.ChooseCell(rgb.PixelData, width, left, top, bits);

      // The bitmap is kept in plain row order here; the writer is what interleaves it back into the
      // thirds the hardware addresses.
      for (var y = 0; y < 8; ++y)
        bitmap[(top + y) * columns + left / 8] = bits[y];
    }

    return new() { BitmapData = bitmap, AttributeData = attributes };
  }
}
