using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.IndyPaint;

/// <summary>Reads IndyPaint screen dumps from bytes, streams, or file paths.</summary>
public static class IndyPaintReader {

  /// <summary>The exact file size of a valid IndyPaint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = IndyPaintFile.ExpectedFileSize;

  public static IndyPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IndyPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IndyPaintFile FromStream(Stream stream) {
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

  public static IndyPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IndyPaintFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an IndyPaint header: got {data.Length} bytes.");

    if (!data[..4].SequenceEqual(IndyPaintFile.Signature))
      throw new InvalidDataException("Not an IndyPaint picture: it does not open with Indy.");

    // The header says how big it is, and the samples are 320 and 384 across alike. Taking one fixed
    // length instead refused every picture that was not the commoner of the two.
    var width = BinaryPrimitives.ReadUInt16BigEndian(data[IndyPaintFile.DimensionsOffset..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[(IndyPaintFile.DimensionsOffset + 2)..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid IndyPaint size: {width}x{height}.");

    var pixelBytes = width * height * IndyPaintFile.BytesPerPixel;
    if (data.Length != IndyPaintFile.HeaderSize + pixelBytes)
      throw new InvalidDataException(
        $"An IndyPaint picture of {width}x{height} is {IndyPaintFile.HeaderSize + pixelBytes} bytes, got {data.Length}.");

    return new IndyPaintFile {
      Width = width,
      Height = height,
      PixelData = data.Slice(IndyPaintFile.HeaderSize, pixelBytes).ToArray(),
    };
  }

  public static IndyPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
