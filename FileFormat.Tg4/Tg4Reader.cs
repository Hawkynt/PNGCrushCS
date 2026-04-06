using System;
using System.IO;

namespace FileFormat.Tg4;

/// <summary>Reads TG4 files from bytes, streams, or file paths.</summary>
public static class Tg4Reader {

  public static Tg4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TG4 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Tg4File FromStream(Stream stream) {
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

  public static Tg4File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Tg4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Tg4File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid TG4 file (need at least {Tg4File.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != Tg4File.Magic[0] || data[1] != Tg4File.Magic[1] || data[2] != Tg4File.Magic[2] || data[3] != Tg4File.Magic[3])
      throw new InvalidDataException("Invalid TG4 magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid TG4 dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < Tg4File.HeaderSize + pixelDataSize)
      throw new InvalidDataException("TG4 file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(Tg4File.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
