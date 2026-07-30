using System;
using FileFormat.Core;

namespace FileFormat.MsxGl8;

/// <summary>In-memory representation of a sized-header MSX2 Screen 8 picture (.gl8, .sh8).</summary>
/// <remarks>
/// A four-byte header giving the dimensions, then one byte per pixel. Unlike the other GL formats
/// there is no palette anywhere, not even in a companion file: Screen 8 spends its byte on the
/// colour itself rather than on an index, so the picture is already true colour of a sort — three
/// bits of green, three of red, two of blue.
/// </remarks>
public readonly record struct MsxGl8File
  : IImageFormatReader<MsxGl8File>, IImageToRawImage<MsxGl8File>,
    IImageFromRawImage<MsxGl8File>, IImageFormatWriter<MsxGl8File> {

  /// <summary>Size of the header: width then height, each a little-endian 16-bit value.</summary>
  public const int HeaderSize = 4;

  /// <summary>Largest picture we accept, guarding against a corrupt header claiming gigabytes.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<MsxGl8File>.PrimaryExtension => ".gl8";
  static string[] IImageFormatMetadata<MsxGl8File>.FileExtensions => [".gl8", ".sh8"];
  static MsxGl8File IImageFormatReader<MsxGl8File>.FromSpan(ReadOnlySpan<byte> data) => MsxGl8Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxGl8File>.ToBytes(MsxGl8File file) => MsxGl8Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxGl8File>.VideoModes => [
    new("Screen 8", [(256, 212)], [256])
  ];

  /// <summary>Picture width.</summary>
  public int Width { get; init; }

  /// <summary>Picture height.</summary>
  public int Height { get; init; }

  /// <summary>The bitmap, one byte per pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MsxGl8File file) {
    var expected = file.Width * file.Height;
    var pixels = new byte[expected];
    var data = file.PixelData ?? [];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.Screen8Palette(),
      PaletteCount = 256,
    };
  }

  public static MsxGl8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1 || image.Width > MaxDimension || image.Height > MaxDimension)
      throw new ArgumentException($"A Screen 8 picture is at most {MaxDimension}x{MaxDimension}, got {image.Width}x{image.Height}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var palette = MsxGraphics.Screen8Palette();
    var data = new byte[image.Width * image.Height];

    // The colour is the byte, so encoding is a nearest-colour search over the fixed 256 rather
    // than a palette to choose.
    for (var i = 0; i < data.Length; ++i)
      data[i] = _Nearest(palette, rgb.PixelData[i * 3], rgb.PixelData[i * 3 + 1], rgb.PixelData[i * 3 + 2]);

    return new() { Width = image.Width, Height = image.Height, PixelData = data };
  }

  private static byte _Nearest(ReadOnlySpan<byte> palette, byte red, byte green, byte blue) {
    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < 256; ++i) {
      int dr = palette[i * 3] - red, dg = palette[i * 3 + 1] - green, db = palette[i * 3 + 2] - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
    }

    return (byte)best;
  }
}
