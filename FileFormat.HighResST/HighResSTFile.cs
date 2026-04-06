using System;
using FileFormat.Core;

namespace FileFormat.HighResST;

/// <summary>In-memory representation of a HighRes ST monochrome image (Atari ST, 640x400, 1 bitplane).</summary>
public readonly record struct HighResSTFile : IImageFormatReader<HighResSTFile>, IImageToRawImage<HighResSTFile>, IImageFromRawImage<HighResSTFile>, IImageFormatWriter<HighResSTFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 640;
  private const int _HEIGHT = 400;
  private const int _NUM_PLANES = 1;
  private const int _BYTES_PER_ROW = 80;

  static string IImageFormatMetadata<HighResSTFile>.PrimaryExtension => ".hst";
  static string[] IImageFormatMetadata<HighResSTFile>.FileExtensions => [".hst", ".hrs"];
  static HighResSTFile IImageFormatReader<HighResSTFile>.FromSpan(ReadOnlySpan<byte> data) => HighResSTReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<HighResSTFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<HighResSTFile>.ToBytes(HighResSTFile file) => HighResSTWriter.ToBytes(file);

  /// <summary>Image width (always 640).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 400).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values (only first 2 entries used for mono).</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of monochrome planar pixel data (1 bitplane, 80 bytes/row, 400 rows).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(HighResSTFile file) {

    // 1bpp mono: each byte encodes 8 pixels, MSB first
    // Output as Indexed1 with B&W palette
    var rowBytes = (_WIDTH + 7) / 8;
    var outputData = new byte[rowBytes * _HEIGHT];

    // For high-res mono, pixel data is simply 1 bitplane = 80 bytes/row x 400 rows
    // In ST high-res the planar data is linear (1 plane, no interleave)
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, outputData.Length)).CopyTo(outputData.AsSpan(0));

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Indexed1,
      PixelData = outputData,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  public static HighResSTFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    if (image.Width != _WIDTH)
      throw new ArgumentException($"HighRes ST images must be exactly {_WIDTH} pixels wide.", nameof(image));
    if (image.Height != _HEIGHT)
      throw new ArgumentException($"HighRes ST images must be exactly {_HEIGHT} pixels tall.", nameof(image));

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    var rowBytes = (_WIDTH + 7) / 8;
    var copyLen = Math.Min(rowBytes * _HEIGHT, _PIXEL_DATA_SIZE);
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, copyLen)).CopyTo(pixelData.AsSpan(0));

    // Default mono palette: 0=white, 1=black (ST convention)
    var palette = new short[16];
    palette[0] = 0x0777; // white
    palette[1] = 0x0000; // black

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      PixelData = pixelData,
      Palette = palette,
    };
  }
}
