using System;
using FileFormat.Core;

namespace FileFormat.Crw;

/// <summary>In-memory representation of a Canon CIFF raw file (.crw).</summary>
/// <remarks>
/// A CIFF file is a heap with its directory at the end: the last four bytes of a heap give the
/// offset of a record table, and each record states a type, a length and where its content sits
/// relative to the heap's own base. That is enough to find everything without knowing a camera:
/// record 0x1031 states the sensor's dimensions and the borders of the picture within it, 0x1810
/// states the size the camera means to produce, and the two agree to the pixel in every sample here
/// — 3143 minus 72 plus one is 3072, which is what the camera says the picture is. Record 0x1835
/// picks one of three Huffman table pairs and 0x2005 holds the sensor data itself.
/// <para/>
/// The sensor data is Canon's own compression: a difference per pixel against the pixel two to its
/// left, Huffman-coded in blocks of sixty-four in the manner of JPEG's AC coefficients, with the
/// low two bits of each sample held apart in a plane of their own on the twelve-bit bodies.
/// </remarks>
public readonly record struct CrwFile : IImageFormatReader<CrwFile>, IImageToRawImage<CrwFile> {

  static string IImageFormatMetadata<CrwFile>.PrimaryExtension => ".crw";
  static string[] IImageFormatMetadata<CrwFile>.FileExtensions => [".crw"];
  static CrwFile IImageFormatReader<CrwFile>.FromSpan(ReadOnlySpan<byte> data) => CrwReader.FromSpan(data);

  static bool? IImageFormatMetadata<CrwFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 14)
      return null;

    return (header[0] == 'I' && header[1] == 'I' || header[0] == 'M' && header[1] == 'M')
      && header[6..14].SequenceEqual("HEAPCCDR"u8)
        ? true
        : null;
  }

  /// <summary>Width of the picture the camera states, which is the sensor less its borders.</summary>
  public int Width { get; init; }

  /// <summary>Height of the picture the camera states.</summary>
  public int Height { get; init; }

  /// <summary>The developed picture, three bytes a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The sensor's own samples, one per pixel of the full sensor, before any interpolation.</summary>
  public ushort[] Sensor { get; init; }

  /// <summary>Width of the sensor, which is wider than the picture.</summary>
  public int SensorWidth { get; init; }

  /// <summary>Height of the sensor.</summary>
  public int SensorHeight { get; init; }

  public static RawImage ToRawImage(CrwFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
