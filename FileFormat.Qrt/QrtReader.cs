using System;
using System.IO;

namespace FileFormat.Qrt;

/// <summary>Reads QRT Ray Tracer files from bytes, streams, or file paths.</summary>
public static class QrtReader {

  public static QrtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QRT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QrtFile FromStream(Stream stream) {
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

  public static QrtFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < QrtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid QRT file.");

    var header = QrtHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("QRT image dimensions must be non-zero.");

    var expectedPixelBytes = width * height * 3;
    var available = data.Length - QrtHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(QrtHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new QrtFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static QrtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < QrtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid QRT file.");

    var header = QrtHeader.ReadFrom(data.AsSpan());
    var width = header.Width;
    var height = header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("QRT image dimensions must be non-zero.");

    var expectedPixelBytes = width * height * 3;
    var available = data.Length - QrtHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(QrtHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new QrtFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
