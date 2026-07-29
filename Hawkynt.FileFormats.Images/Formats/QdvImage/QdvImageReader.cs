using System;
using System.IO;

namespace FileFormat.QdvImage;

/// <summary>Reads QDV image files from bytes, streams, or file paths.</summary>
public static class QdvImageReader {

  public static QdvImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QDV file not found.", file.FullName);

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
      throw new InvalidDataException($"Data too small for a valid QDV file (need at least {QdvImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != QdvImageFile.Magic[0] || data[1] != QdvImageFile.Magic[1] || data[2] != QdvImageFile.Magic[2] || data[3] != QdvImageFile.Magic[3])
      throw new InvalidDataException("Invalid QDV magic bytes.");

    var header = QdvImageHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;
    var bpp = header.Bpp;
    var flags = header.Flags;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid QDV dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - QdvImageFile.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.Slice(QdvImageFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Flags = flags,
      PixelData = pixelData,
    };
  }

  public static QdvImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
