using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Vivid;

/// <summary>Reads QRT / Vivid ray tracer output from bytes, streams, or file paths.</summary>
public static class VividReader {

  public static VividFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Vivid picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VividFile FromStream(Stream stream) {
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

  public static VividFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < VividFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a Vivid picture (got {data.Length} bytes).");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Vivid picture of {width}x{height} is no size.");

    // Each row states its own number and then its three colours a plane at a time.
    var stride = VividFile.RowNumberSize + width * 3;
    var wanted = VividFile.HeaderSize + stride * height;
    if (data.Length < wanted)
      throw new InvalidDataException($"A Vivid picture of {width}x{height} takes {wanted} bytes; this file is {data.Length}.");

    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      var row = VividFile.HeaderSize + y * stride + VividFile.RowNumberSize;
      for (var x = 0; x < width; ++x) {
        var to = (y * width + x) * 3;
        rgb[to] = data[row + x];
        rgb[to + 1] = data[row + width + x];
        rgb[to + 2] = data[row + width * 2 + x];
      }
    }

    return new() { Width = width, Height = height, PixelData = rgb };
  }

  public static VividFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
