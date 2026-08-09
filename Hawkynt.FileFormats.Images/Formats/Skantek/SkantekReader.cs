using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.Skantek;

/// <summary>Reads Skantek pages from bytes, streams, or file paths.</summary>
public static class SkantekReader {

  private const int _MaxDimension = 65535;

  public static SkantekFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Skantek file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SkantekFile FromStream(Stream stream) {
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

  public static SkantekFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SkantekFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= SkantekFile.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a Skantek page (more than {SkantekFile.HeaderSize} bytes are needed, got {data.Length}).");

    if (!data[..SkantekFile.Signature.Length].SequenceEqual(SkantekFile.Signature))
      throw new InvalidDataException("Not a Skantek page: the four longs it opens with are not the format's.");

    if (!data.Slice(SkantekFile.StampOffset, SkantekFile.Stamp.Length).SequenceEqual(SkantekFile.Stamp))
      throw new InvalidDataException("Not a Skantek page: the six characters at offset 302 are not 920101.");

    var height = BinaryPrimitives.ReadInt32BigEndian(data[SkantekFile.HeightOffset..]);
    var width = BinaryPrimitives.ReadInt32BigEndian(data[SkantekFile.WidthOffset..]);
    if (width < 1 || height < 1 || width > _MaxDimension || height > _MaxDimension)
      throw new InvalidDataException($"A Skantek page states a picture of {width}x{height}.");

    // The coding runs from the bottom bit of each byte upwards, so every byte is turned over before
    // it reaches a decoder that reads from the top down.
    var coded = CcittFillOrder.Reverse(data[SkantekFile.HeaderSize..]);
    var pixelData = CcittG4Decoder.Decode(coded, width, height, out var rowsDecoded);
    if (rowsDecoded != height)
      throw new InvalidDataException(
        $"A Skantek page's Group 4 coding runs out after {rowsDecoded} of the {height} rows its header states.");

    return new() { Width = width, Height = height, PixelData = pixelData };
  }
}
