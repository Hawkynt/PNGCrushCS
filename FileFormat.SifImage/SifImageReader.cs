using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.SifImage;

/// <summary>Reads SIF image files from bytes, streams, or file paths.</summary>
public static class SifImageReader {

  public static SifImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SifImageFile FromStream(Stream stream) {
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

  public static SifImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SifImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SIF file (need at least {SifImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != SifImageFile.Magic[0] || data[1] != SifImageFile.Magic[1] || data[2] != SifImageFile.Magic[2] || data[3] != SifImageFile.Magic[3])
      throw new InvalidDataException("Invalid SIF magic bytes.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var bpp = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SIF dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - SifImageFile.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.Slice(SifImageFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }

  public static SifImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
