using System;
using System.IO;

namespace FileFormat.SonyMavica;

/// <summary>Reads Sony Mavica .411 files from bytes, streams, or file paths.</summary>
public static class SonyMavicaReader {

  public static SonyMavicaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mavica file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SonyMavicaFile FromStream(Stream stream) {
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

  public static SonyMavicaFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SonyMavicaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SonyMavicaFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Mavica file (need at least {SonyMavicaFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != SonyMavicaFile.Magic[0] || data[1] != SonyMavicaFile.Magic[1])
      throw new InvalidDataException("Invalid Mavica magic bytes.");

    var width = BitConverter.ToUInt16(data, 2);
    var height = BitConverter.ToUInt16(data, 4);
    var format = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Mavica dimensions: {width}x{height}.");

    var pixelDataSize = width * height * 3;
    if (data.Length < SonyMavicaFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("Mavica file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(SonyMavicaFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Format = format,
      PixelData = pixelData,
    };
  }
}
