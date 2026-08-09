using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Iss;

/// <summary>Reads ISS pictures (.iss) from bytes, streams, or file paths.</summary>
public static class IssReader {

  public static IssFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ISS picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IssFile FromStream(Stream stream) {
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

  public static IssFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IssFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IssFile.PixelsOffset)
      throw new InvalidDataException($"Data too small for an ISS picture (need at least {IssFile.PixelsOffset} bytes, got {data.Length}).");

    if (!data[..IssFile.Magic.Length].SequenceEqual(IssFile.Magic))
      throw new InvalidDataException("Not an ISS picture: the eight characters it opens with are not the ones this format uses.");

    var kind = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
    if (kind is not (IssFile.MonochromeKind or IssFile.GrayscaleKind))
      throw new InvalidDataException($"ISS: picture kind {kind} is not one of the two this format has.");

    var height = BinaryPrimitives.ReadUInt32BigEndian(data[18..]);
    var width = BinaryPrimitives.ReadUInt32BigEndian(data[22..]);
    if (width is < 1 or > int.MaxValue / 4 || height is < 1 or > int.MaxValue / 4)
      throw new InvalidDataException($"Invalid ISS dimensions: {width}x{height}.");

    var stride = (long)IssFile.RowStride(kind, (int)width);
    var needed = stride * height;
    if (needed > data.Length - IssFile.PixelsOffset)
      throw new InvalidDataException($"A {width}x{height} ISS picture needs {needed} bytes and the file has {data.Length - IssFile.PixelsOffset} behind its header.");

    return new() {
      Width = (int)width,
      Height = (int)height,
      Kind = kind,
      PixelData = data.Slice(IssFile.PixelsOffset, (int)needed).ToArray(),
    };
  }
}
