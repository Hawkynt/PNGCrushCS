using System;
using System.IO;

namespace FileFormat.QuantelVpb;

/// <summary>Reads Quantel VPB image files from bytes, streams, or file paths.</summary>
public static class QuantelVpbReader {

  public static QuantelVpbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VPB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QuantelVpbFile FromStream(Stream stream) {
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

  public static QuantelVpbFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static QuantelVpbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < QuantelVpbFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid VPB file (need at least {QuantelVpbFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != QuantelVpbFile.Magic[0] || data[1] != QuantelVpbFile.Magic[1] || data[2] != QuantelVpbFile.Magic[2] || data[3] != QuantelVpbFile.Magic[3])
      throw new InvalidDataException("Invalid VPB magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var bpp = BitConverter.ToUInt16(data, 8);
    var fields = BitConverter.ToUInt16(data, 10);
    var reserved = BitConverter.ToUInt32(data, 12);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid VPB dimensions: {width}x{height}.");

    var pixelDataSize = width * height * 3;
    if (data.Length < QuantelVpbFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("VPB file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(QuantelVpbFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Fields = fields,
      Reserved = reserved,
      PixelData = pixelData,
    };
  }
}
