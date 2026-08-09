using System;
using System.IO;

namespace FileFormat.ApolloHdru;

/// <summary>Reads Apollo HDRU pages (.hdru, .gn) from bytes, streams, or file paths.</summary>
public static class ApolloHdruReader {

  private static readonly string[] _CompressionNames = ["none", "CCITT Group 3", "CCITT Group 4"];

  public static ApolloHdruFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Apollo HDRU page not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ApolloHdruFile FromStream(Stream stream) {
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

  public static ApolloHdruFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ApolloHdruFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ApolloHdruFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an Apollo HDRU page (need at least {ApolloHdruFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..ApolloHdruFile.Magic.Length].SequenceEqual(ApolloHdruFile.Magic))
      throw new InvalidDataException("Not an Apollo HDRU page: it does not open with 01 01.");

    var compression = _Read16(data, 2);
    var resolution = _Read16(data, 4);
    var width = _Read16(data, 6);
    var height = _Read16(data, 8);

    if (width is < 1 or > ApolloHdruFile.MaximumSide || height is < 1 or > ApolloHdruFile.MaximumSide)
      throw new InvalidDataException($"Invalid Apollo HDRU dimensions: {width}x{height}.");

    if (compression != ApolloHdruFile.Uncompressed)
      throw new InvalidDataException(
        compression < _CompressionNames.Length
          ? $"An Apollo HDRU page compressed with {_CompressionNames[compression]} is not read: where its code stream begins could not be established without a file to check it against."
          : $"An Apollo HDRU page states compression {compression}, which is not one of the three the format has.");

    var stride = (width + 7) / 8;
    var needed = (long)stride * height;
    if (data.Length - ApolloHdruFile.HeaderSize < needed)
      throw new InvalidDataException($"A {width}x{height} page needs {needed} bytes and the file has {data.Length - ApolloHdruFile.HeaderSize} behind its header.");

    var pixels = new byte[needed];
    data.Slice(ApolloHdruFile.HeaderSize, (int)needed).CopyTo(pixels);

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      PixelData = pixels,
    };
  }

  private static int _Read16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];
}
