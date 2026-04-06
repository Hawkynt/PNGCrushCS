using System;
using System.IO;

namespace FileFormat.Rlc2;

/// <summary>Reads RLC2 image files from bytes, streams, or file paths.</summary>
public static class Rlc2Reader {

  public static Rlc2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RLC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Rlc2File FromStream(Stream stream) {
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

  public static Rlc2File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Rlc2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Rlc2File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid RLC2 file (need at least {Rlc2File.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != Rlc2File.Magic[0] || data[1] != Rlc2File.Magic[1] || data[2] != Rlc2File.Magic[2] || data[3] != Rlc2File.Magic[3])
      throw new InvalidDataException("Invalid RLC2 magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var bpp = BitConverter.ToUInt16(data, 8);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid RLC2 dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - Rlc2File.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.AsSpan(Rlc2File.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }
}
