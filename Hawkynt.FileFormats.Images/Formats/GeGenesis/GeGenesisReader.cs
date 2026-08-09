using System;
using System.IO;

namespace FileFormat.GeGenesis;

/// <summary>Reads GE Genesis 5.x images (.fre) from bytes, streams, or file paths.</summary>
public static class GeGenesisReader {

  public static GeGenesisFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GE Genesis file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GeGenesisFile FromStream(Stream stream) {
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

  public static GeGenesisFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static GeGenesisFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < GeGenesisFile.MinimumHeaderSize)
      throw new InvalidDataException($"Data too small for a GE Genesis image (need at least {GeGenesisFile.MinimumHeaderSize} bytes, got {data.Length}).");

    if (!data[..GeGenesisFile.Magic.Length].SequenceEqual(GeGenesisFile.Magic))
      throw new InvalidDataException("Not a GE Genesis image: it does not open with IMGF.");

    var pixelOffset = _ReadBigEndian(data, 4);
    var width = _ReadBigEndian(data, 8);
    var height = _ReadBigEndian(data, 12);
    var depth = _ReadBigEndian(data, 16);
    var compression = _ReadBigEndian(data, 20);

    if (pixelOffset < GeGenesisFile.MinimumHeaderSize || pixelOffset > data.Length)
      throw new InvalidDataException($"The header states the picture starts at {pixelOffset}, which is not inside a file of {data.Length} bytes.");

    if (width is <= 0 or > ushort.MaxValue || height is <= 0 or > ushort.MaxValue)
      throw new InvalidDataException($"Invalid GE Genesis dimensions: {width}x{height}.");

    if (depth is not (8 or 16))
      throw new InvalidDataException($"A GE Genesis image of {depth} bits per sample is not read; 8 and 16 are.");

    var expected = (long)width * height * (depth / 8);
    var available = data.Length - pixelOffset;

    // The compression code is not trusted to say what the bytes are. The file measured here states 1
    // where the published list puts "as is" at 0, so what decides is the arithmetic: an uncompressed
    // picture accounts for the rest of the file exactly, and every compressed one is shorter.
    if (available != expected)
      throw new InvalidDataException(
        $"A GE Genesis image of {width}x{height} at {depth} bits needs {expected} bytes and the file has {available} behind its header, so it is not stored uncompressed (compression code {compression}).");

    var pixels = new byte[expected];
    data.Slice(pixelOffset, (int)expected).CopyTo(pixels);

    return new() {
      Width = width,
      Height = height,
      Depth = depth,
      PixelData = pixels,
    };
  }

  private static int _ReadBigEndian(ReadOnlySpan<byte> data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
