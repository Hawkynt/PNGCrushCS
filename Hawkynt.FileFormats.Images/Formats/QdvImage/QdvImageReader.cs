using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.QdvImage;

/// <summary>Reads QDV pictures from bytes, streams, or file paths.</summary>
public static class QdvImageReader {

  public static QdvImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QDV picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QdvImageFile FromStream(Stream stream) {
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

  public static QdvImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < QdvImageFile.MinFileSize)
      throw new InvalidDataException(
        $"Data too small for a valid QDV file (need at least {QdvImageFile.MinFileSize} bytes, got {data.Length}).");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid QDV dimensions: {width}x{height}.");

    // Nothing in the file says what it is, so the size stated has to account for the whole of it.
    var needed = QdvImageFile.PixelOffset + width * height;
    if (data.Length != needed)
      throw new InvalidDataException($"A {width}x{height} QDV picture is {needed} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      HighestIndex = data[4],
      Palette = data.Slice(QdvImageFile.HeaderSize, QdvImageFile.PaletteSize).ToArray(),
      PixelData = data.Slice(QdvImageFile.PixelOffset, width * height).ToArray(),
    };
  }

  public static QdvImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
