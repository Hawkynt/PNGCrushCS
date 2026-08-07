using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.It01;

/// <summary>Reads "IT01" pictures from bytes, streams, or file paths.</summary>
public static class It01Reader {

  public static It01File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IT01 picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static It01File FromStream(Stream stream) {
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

  public static It01File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < It01File.DefaultDataOffset || !data[..4].SequenceEqual(It01File.Magic))
      throw new InvalidDataException("Not an IT01 picture: it does not open with IT01.");

    var width = BinaryPrimitives.ReadInt32BigEndian(data[It01File.WidthAt..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(data[It01File.HeightAt..]);
    var bands = BinaryPrimitives.ReadInt32BigEndian(data[It01File.BandsAt..]);
    var offset = BinaryPrimitives.ReadInt32BigEndian(data[It01File.DataOffsetAt..]);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid IT01 size: {width}x{height}.");
    if (bands is not (1 or 3))
      throw new InvalidDataException($"Invalid IT01 band count: {bands}. Expected 1 or 3.");

    // The header states where the picture begins rather than being a fixed length, so it is read
    // rather than assumed — but a nonsense value would send the copy off the end.
    if (offset < It01File.DefaultDataOffset || offset > data.Length)
      throw new InvalidDataException($"Invalid IT01 data offset: {offset}.");

    var needed = (long)width * height * bands;
    if (data.Length - offset < needed)
      throw new InvalidDataException(
        $"A {width}x{height} IT01 picture in {bands} band(s) needs {needed} bytes of picture, got {data.Length - offset}.");

    return new() {
      Width = width,
      Height = height,
      Bands = bands,
      PixelData = data.Slice(offset, (int)needed).ToArray(),
    };
  }

  public static It01File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
