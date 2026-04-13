using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pic2;

/// <summary>Reads PIC2 files from bytes, streams, or file paths.</summary>
public static class Pic2Reader {

  public static Pic2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PIC2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Pic2File FromStream(Stream stream) {
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

  public static Pic2File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Pic2File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid PIC2 file (need at least {Pic2File.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != Pic2File.Magic[0] || data[1] != Pic2File.Magic[1] || data[2] != Pic2File.Magic[2] || data[3] != Pic2File.Magic[3])
      throw new InvalidDataException("Invalid PIC2 magic bytes.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var bpp = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var mode = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid PIC2 dimensions: {width}x{height}.");

    var bytesPerPixel = bpp / 8;
    if (bytesPerPixel == 0)
      bytesPerPixel = 3;

    var pixelDataSize = width * height * bytesPerPixel;
    if (data.Length < Pic2File.HeaderSize + pixelDataSize)
      throw new InvalidDataException("PIC2 file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(Pic2File.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Mode = mode,
      PixelData = pixelData,
    };
  }

  public static Pic2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
