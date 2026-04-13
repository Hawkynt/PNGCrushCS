using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.VentaFax;

/// <summary>Reads VentaFax VFX files from bytes, streams, or file paths.</summary>
public static class VentaFaxReader {

  public static VentaFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VFX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VentaFaxFile FromStream(Stream stream) {
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

  public static VentaFaxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < VentaFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid VFX file (need at least {VentaFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != VentaFaxFile.Magic[0] || data[1] != VentaFaxFile.Magic[1] || data[2] != VentaFaxFile.Magic[2] || data[3] != VentaFaxFile.Magic[3])
      throw new InvalidDataException("Invalid VFX magic bytes.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var encoding = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid VFX dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < VentaFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("VFX file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(VentaFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Encoding = encoding,
      PixelData = pixelData,
    };
  }

  public static VentaFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
