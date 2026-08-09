using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.CoreIdc;

/// <summary>Reads Core IDC pictures (.idc) from bytes, streams, or file paths.</summary>
public static class CoreIdcReader {

  /// <summary>The plane counts this reader takes; XnView also reads two and four and shows only three of them.</summary>
  private static readonly int[] _SupportedPlanes = [1, 3];

  /// <summary>The depths this reader takes.</summary>
  private static readonly int[] _SupportedDepths = [1, 4, 8, 24];

  public static CoreIdcFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Core IDC picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CoreIdcFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static CoreIdcFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CoreIdcFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CoreIdcFile.TrailerSize)
      throw new InvalidDataException($"Data too small for a Core IDC picture (need at least {CoreIdcFile.TrailerSize} bytes, got {data.Length}).");

    var trailer = data[^CoreIdcFile.TrailerSize..];
    if (!trailer.Slice(CoreIdcFile.TrailerSize - CoreIdcFile.SignatureFromEnd, CoreIdcFile.Signature.Length).SequenceEqual(CoreIdcFile.Signature))
      throw new InvalidDataException("Not a Core IDC picture: the five characters that stand eight bytes before the end are not the ones this format uses.");

    var width = BinaryPrimitives.ReadUInt32BigEndian(trailer);
    var height = BinaryPrimitives.ReadUInt32BigEndian(trailer[4..]);
    var planes = BinaryPrimitives.ReadUInt16BigEndian(trailer[8..]);
    var depth = BinaryPrimitives.ReadUInt16BigEndian(trailer[10..]);

    if (width is < 1 or > int.MaxValue / 4 || height is < 1 or > int.MaxValue / 4)
      throw new InvalidDataException($"Invalid Core IDC dimensions: {width}x{height}.");

    if (Array.IndexOf(_SupportedPlanes, (int)planes) < 0)
      throw new InvalidDataException($"Core IDC: {planes} planes is not a count this reads.");

    if (Array.IndexOf(_SupportedDepths, (int)depth) < 0)
      throw new InvalidDataException($"Core IDC: {depth} bits a pixel is not a depth this reads.");

    if (planes == 3 && depth != 8)
      throw new InvalidDataException($"Core IDC: three planes are only read at eight bits a pixel, not {depth}.");

    var stride = ((long)width * depth + 7) / 8;
    var needed = stride * height * planes;
    if (needed > data.Length - CoreIdcFile.TrailerSize)
      throw new InvalidDataException($"A {width}x{height} Core IDC picture needs {needed} bytes and the file holds {data.Length - CoreIdcFile.TrailerSize} before its trailer.");

    return new() {
      Width = (int)width,
      Height = (int)height,
      Planes = planes,
      BitsPerPixel = depth,
      PixelData = data[..(int)needed].ToArray(),
    };
  }
}
