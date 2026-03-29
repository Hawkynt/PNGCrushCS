using System;
using System.IO;

namespace FileFormat.Cineon;

/// <summary>Reads Cineon files from bytes, streams, or file paths.</summary>
public static class CineonReader {

  public static CineonFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cineon file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CineonFile FromStream(Stream stream) {
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

  public static CineonFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CineonHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Cineon file.");

    var span = data.AsSpan();
    var header = CineonHeader.ReadFrom(span);

    if (header.Magic != CineonHeader.MagicNumber)
      throw new InvalidDataException("Invalid Cineon magic number.");

    var dataOffset = header.ImageDataOffset;
    if (dataOffset < CineonHeader.StructSize)
      dataOffset = CineonHeader.StructSize;

    var pixelDataLength = data.Length - dataOffset;
    var pixelData = new byte[pixelDataLength];
    if (pixelDataLength > 0)
      data.AsSpan(dataOffset, pixelDataLength).CopyTo(pixelData.AsSpan(0));

    return new CineonFile {
      Width = header.PixelsPerLine,
      Height = header.LinesPerElement,
      BitsPerSample = header.BitsPerSample,
      Orientation = header.Orientation,
      ImageDataOffset = dataOffset,
      PixelData = pixelData
    };
  }
}
