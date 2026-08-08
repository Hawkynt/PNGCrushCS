using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Vitec;

/// <summary>Reads VITec images from bytes, streams, or file paths.</summary>
public static class VitecReader {

  /// <summary>Where the width sits, counted from the start of the second header.</summary>
  private const int _WidthOffset = 32;

  /// <summary>Where the height sits, counted from the start of the second header.</summary>
  private const int _HeightOffset = 36;

  /// <summary>Where the sample count sits, counted from the start of the second header.</summary>
  private const int _SamplesOffset = 52;

  /// <summary>Longer than any header these carry, and it bounds a false match.</summary>
  private const int _MaxHeaderLength = 1 << 16;

  public static VitecFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VITec image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VitecFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static VitecFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < VitecFile.NameOffset + 5 || !data[..4].SequenceEqual(VitecFile.Magic)
        || !data.Slice(VitecFile.NameOffset, 5).SequenceEqual(VitecFile.Name))
      throw new InvalidDataException("Not a VITec image: it does not carry the VITec header.");

    var firstHeader = (int)BinaryPrimitives.ReadUInt32BigEndian(data[VitecFile.FirstHeaderLengthOffset..]);
    if (firstHeader is < 0 or > _MaxHeaderLength || 8 + firstHeader > data.Length)
      throw new InvalidDataException($"A VITec image states a first header of {firstHeader} bytes.");

    // Each length counts the four bytes it is written in, so the second header starts where the
    // first one's count runs out and the samples start where the second's does.
    var secondAt = 4 + firstHeader;
    if (secondAt + 4 > data.Length)
      throw new InvalidDataException($"A VITec image states a first header of {firstHeader} bytes.");

    var secondHeader = (int)BinaryPrimitives.ReadUInt32BigEndian(data[secondAt..]);
    if (secondHeader < _SamplesOffset + 8 || secondHeader > _MaxHeaderLength || secondAt + secondHeader > data.Length)
      throw new InvalidDataException($"A VITec image states a second header of {secondHeader} bytes.");

    var fields = secondAt + 4;
    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(fields + _WidthOffset)..]);
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(fields + _HeightOffset)..]);
    var samples = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(fields + _SamplesOffset)..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"A VITec image states a size of {width} by {height}.");
    if (samples is not (1 or 3))
      throw new InvalidDataException($"A VITec image of {samples} samples to the pixel is not one this reads.");

    // The two headers and the samples have to be the whole file. That is what says these offsets are
    // being read as the format means them rather than landing on plausible numbers.
    var expected = (long)width * height * samples;
    var pixelsAt = secondAt + secondHeader;
    if (pixelsAt + expected != data.Length)
      throw new InvalidDataException(
        $"A VITec image of {width} by {height} by {samples} needs {pixelsAt + expected} bytes and the file is {data.Length}.");

    // And the second header says the same thing a second way: the size it gives for the data counts
    // itself along with the samples.
    var statedData = (long)BinaryPrimitives.ReadUInt32BigEndian(data[fields..]);
    if (statedData != secondHeader + expected)
      throw new InvalidDataException($"A VITec image states {statedData} bytes of data where its size makes {secondHeader + expected}.");

    var pixels = new byte[expected];
    data.Slice(pixelsAt, (int)expected).CopyTo(pixels);

    return new() {
      Width = width,
      Height = height,
      Samples = samples,
      PixelData = pixels,
    };
  }

  public static VitecFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
