using System;
using System.IO;

namespace FileFormat.SegaSj1;

/// <summary>Reads Sega SJ1 files from bytes, streams, or file paths.</summary>
public static class SegaSj1Reader {

  public static SegaSj1File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SJ1 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SegaSj1File FromStream(Stream stream) {
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

  public static SegaSj1File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SegaSj1File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SJ1 file (need at least {SegaSj1File.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != SegaSj1File.Magic[0] || data[1] != SegaSj1File.Magic[1] || data[2] != SegaSj1File.Magic[2] || data[3] != SegaSj1File.Magic[3])
      throw new InvalidDataException("Invalid SJ1 magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var bpp = BitConverter.ToUInt16(data, 8);
    var flags = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SJ1 dimensions: {width}x{height}.");

    var bytesPerPixel = bpp / 8;
    if (bytesPerPixel == 0)
      bytesPerPixel = 3;

    var pixelDataSize = width * height * bytesPerPixel;
    if (data.Length < SegaSj1File.HeaderSize + pixelDataSize)
      throw new InvalidDataException("SJ1 file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(SegaSj1File.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
