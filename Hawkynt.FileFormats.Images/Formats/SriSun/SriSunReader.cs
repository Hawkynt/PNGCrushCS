using System;
using System.IO;

namespace FileFormat.SriSun;

/// <summary>Reads SriSun pictures (.ssi) from bytes, streams, or file paths.</summary>
public static class SriSunReader {

  public static SriSunFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SriSun picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SriSunFile FromStream(Stream stream) {
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

  public static SriSunFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SriSunFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SriSunFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a SriSun picture (need at least {SriSunFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..SriSunFile.Magic.Length].SequenceEqual(SriSunFile.Magic))
      throw new InvalidDataException("Not a SriSun picture: it does not open with srisunim.");

    if (data[SriSunFile.MarkerAt] != SriSunFile.Marker)
      throw new InvalidDataException($"A SriSun picture holds {SriSunFile.Marker} at offset {SriSunFile.MarkerAt} and this one holds {data[SriSunFile.MarkerAt]}.");

    if (data[SriSunFile.DataTypeAt] != 0)
      throw new InvalidDataException($"SriSun data type {data[SriSunFile.DataTypeAt]} is not one this reads; only type 0 has a reading.");

    var depth = data[SriSunFile.DepthAt];
    if (depth is not (1 or 4 or 8 or 16 or 24))
      throw new InvalidDataException($"A SriSun picture is 1, 4, 8, 16 or 24 bits a pixel and this one states {depth}.");

    var width = _Read16(data, SriSunFile.WidthAt);
    var height = _Read16(data, SriSunFile.HeightAt);
    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid SriSun dimensions: {width}x{height}.");

    var stride = SriSunFile.StrideOf(width, depth);
    var needed = (long)SriSunFile.HeaderSize + (long)stride * height;
    if (data.Length < needed)
      throw new InvalidDataException($"A {width}x{height} SriSun picture at {depth} bits needs {needed} bytes and the file has {data.Length}.");

    var pixels = new byte[stride * height];
    data.Slice(SriSunFile.HeaderSize, pixels.Length).CopyTo(pixels);

    return new() {
      Width = width,
      Height = height,
      Depth = depth,
      PixelData = pixels,
    };
  }

  /// <summary>The format states its two lengths the big-endian way round.</summary>
  private static int _Read16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];
}
