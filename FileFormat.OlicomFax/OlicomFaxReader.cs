using System;
using System.IO;

namespace FileFormat.OlicomFax;

/// <summary>Reads OlicomFax OFX files from bytes, streams, or file paths.</summary>
public static class OlicomFaxReader {

  public static OlicomFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("OFX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OlicomFaxFile FromStream(Stream stream) {
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

  public static OlicomFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < OlicomFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid OFX file (need at least {OlicomFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != OlicomFaxFile.Magic[0] || data[1] != OlicomFaxFile.Magic[1] || data[2] != OlicomFaxFile.Magic[2] || data[3] != OlicomFaxFile.Magic[3])
      throw new InvalidDataException("Invalid OFX magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var flags = BitConverter.ToUInt16(data, 8);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid OFX dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < OlicomFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("OFX file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(OlicomFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
