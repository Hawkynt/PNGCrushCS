using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.FirstPublisher;

/// <summary>Reads 1st Publisher clip art from bytes, streams, or file paths.</summary>
public static class FirstPublisherReader {

  public static FirstPublisherFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("1st Publisher file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FirstPublisherFile FromStream(Stream stream) {
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

  public static FirstPublisherFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FirstPublisherFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid 1st Publisher file.");

    // Nothing here identifies the format, so the two unused words and an exact size are all there is
    // to go on. Anything looser would claim files belonging to the other things called ART.
    if (BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0)
      throw new InvalidDataException("1st Publisher header does not start its sizes with a zero word.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid 1st Publisher size {width}x{height}.");

    var expected = (width + 7) / 8 * height;
    var available = data.Length - FirstPublisherFile.HeaderSize;
    if (available != expected)
      throw new InvalidDataException(
        $"1st Publisher {width}x{height} needs exactly {expected} bytes of rows, but {available} follow the header.");

    return new() {
      Width = width,
      Height = height,
      PixelData = data[FirstPublisherFile.HeaderSize..].ToArray(),
    };
  }

  public static FirstPublisherFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
