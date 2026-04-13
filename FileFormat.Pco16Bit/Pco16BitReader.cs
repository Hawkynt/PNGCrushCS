using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pco16Bit;

/// <summary>Reads PCO 16-bit grayscale files from bytes, streams, or file paths.</summary>
public static class Pco16BitReader {

  public static Pco16BitFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("B16 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Pco16BitFile FromStream(Stream stream) {
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

  public static Pco16BitFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Pco16BitFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid B16 file (need at least {Pco16BitFile.MinFileSize} bytes, got {data.Length}).");

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[0..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid B16 dimensions: {width}x{height}.");

    var pixelDataSize = width * height * 2;
    if (data.Length < Pco16BitFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("B16 file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(Pco16BitFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  public static Pco16BitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
