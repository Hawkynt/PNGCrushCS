using System;
using System.Buffers.Binary;

namespace FileFormat.Vitec;

/// <summary>Writes a VITec image: the two headers, then the samples.</summary>
/// <remarks>
/// The lengths are the ones the sample carries — a first header of a hundred and twenty bytes and a
/// second of a hundred and forty-four — because the reader checks that the file is exactly the two
/// headers and the samples, and that the second header's own count of the data agrees with the size
/// it states. Both statements are written from the same numbers the picture gives, so the file
/// closes on itself the way a real one does.
/// </remarks>
public static class VitecWriter {

  /// <summary>Length of the first header, counting the four bytes it is written in.</summary>
  private const int _FirstHeaderLength = 120;

  /// <summary>Length of the second, likewise.</summary>
  private const int _SecondHeaderLength = 144;

  /// <summary>Where the size, the sample count and the data length sit inside the second header.</summary>
  private const int _DataSizeOffset = 0, _WidthOffset = 32, _HeightOffset = 36, _SamplesOffset = 52;

  public static byte[] ToBytes(VitecFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid VITec image size: {width}x{height}.", nameof(file));

    var samples = file.Samples;
    if (samples is not (1 or 3))
      throw new ArgumentException($"A VITec image carries one sample to the pixel or three, not {samples}.", nameof(file));

    // Every field is a big-endian unsigned long, so a picture whose samples do not fit in one is a
    // size the header cannot state at all.
    var expected = (long)width * height * samples;
    if (expected + _SecondHeaderLength > uint.MaxValue)
      throw new ArgumentException($"A VITec image of {width} by {height} by {samples} states {expected} bytes of data, which is more than the header's unsigned long holds.", nameof(file));

    var pixels = file.PixelData ?? new byte[expected];
    if (pixels.Length < expected)
      throw new ArgumentException($"A VITec image of {width} by {height} by {samples} needs {expected} bytes and has {pixels.Length}.", nameof(file));

    var secondAt = 4 + _FirstHeaderLength;
    var fields = secondAt + 4;
    var pixelsAt = secondAt + _SecondHeaderLength;
    var result = new byte[pixelsAt + expected];

    VitecFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(VitecFile.FirstHeaderLengthOffset), _FirstHeaderLength);
    VitecFile.Name.CopyTo(result.AsSpan(VitecFile.NameOffset));

    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(secondAt), _SecondHeaderLength);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(fields + _DataSizeOffset), (uint)(_SecondHeaderLength + expected));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(fields + _WidthOffset), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(fields + _HeightOffset), (uint)height);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(fields + _SamplesOffset), (uint)samples);

    Array.Copy(pixels, 0, result, pixelsAt, expected);
    return result;
  }
}
