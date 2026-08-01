using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariFalconXga;

/// <summary>In-memory representation of an Atari Falcon XGA 16-bit true color (.xga) image.</summary>
/// <remarks>
/// The file is nothing but samples: no magic, no header, no dimensions. It used to be written here
/// with a two-word width and height in front, which is a container of this project's own invention —
/// a shape no Falcon program writes and none can read. The size comes from the length instead, and
/// only the two lengths the format actually has are a picture.
/// </remarks>
public readonly record struct AtariFalconXgaFile : IImageFormatReader<AtariFalconXgaFile>, IImageToRawImage<AtariFalconXgaFile>, IImageFromRawImage<AtariFalconXgaFile>, IImageFormatWriter<AtariFalconXgaFile> {

  static string IImageFormatMetadata<AtariFalconXgaFile>.PrimaryExtension => ".xga";
  static string[] IImageFormatMetadata<AtariFalconXgaFile>.FileExtensions => [".xga"];
  static AtariFalconXgaFile IImageFormatReader<AtariFalconXgaFile>.FromSpan(ReadOnlySpan<byte> data) => AtariFalconXgaReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariFalconXgaFile>.ToBytes(AtariFalconXgaFile file) => AtariFalconXgaWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<AtariFalconXgaFile>.VideoModes => [
    new("Falcon XGA", [(320, 240), (384, 480)], [65536]),
  ];

  /// <summary>Which picture a file of a given length is, there being exactly two.</summary>
  public static (int Width, int Height) SizeOf(int length) => length switch {
    320 * 240 * 2 => (320, 240),
    384 * 480 * 2 => (384, 480),
    _ => throw new InvalidDataException(
      $"An XGA file is 153600 or 368640 bytes and states its size no other way; this one is {length}."),
  };

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AtariFalconXgaFile file) {

    var rgb565 = file.PixelData;
    var pixelCount = file.Width * file.Height;
    var rgb24 = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 2;
      var hi = srcOffset < rgb565.Length ? rgb565[srcOffset] : (byte)0;
      var lo = srcOffset + 1 < rgb565.Length ? rgb565[srcOffset + 1] : (byte)0;
      var packed = (ushort)((hi << 8) | lo);

      var r5 = (packed >> 11) & 0x1F;
      var g6 = (packed >> 5) & 0x3F;
      var b5 = packed & 0x1F;

      var dstOffset = i * 3;
      rgb24[dstOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[dstOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[dstOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static AtariFalconXgaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Whichever of the two screens is closer in shape: the tall one is twice as high as it is
    // otherwise wide, so anything portrait belongs there and everything else on the small one.
    if ((image.Width, image.Height) is not ((320, 240) or (384, 480)))
      image = image.Height * 320 > image.Width * 240 ? image.SampleTo(384, 480) : image.SampleTo(320, 240);

    image = image.EnsureFormat(PixelFormat.Rgb24);

    var rgb24 = image.PixelData;
    var pixelCount = image.Width * image.Height;
    var rgb565 = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 3;
      var r = rgb24[srcOffset];
      var g = rgb24[srcOffset + 1];
      var b = rgb24[srcOffset + 2];

      var r5 = (r >> 3) & 0x1F;
      var g6 = (g >> 2) & 0x3F;
      var b5 = (b >> 3) & 0x1F;
      var packed = (ushort)((r5 << 11) | (g6 << 5) | b5);

      var dstOffset = i * 2;
      rgb565[dstOffset] = (byte)(packed >> 8);
      rgb565[dstOffset + 1] = (byte)(packed & 0xFF);

      // Quantize input in-place to the lossy RGB565 range so the round-trip is bit-exact.
      rgb24[srcOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[srcOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[srcOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = rgb565,
    };
  }
}
