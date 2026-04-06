using System;
using System.IO;

namespace FileFormat.HomeworldLif;

/// <summary>Reads Homeworld LIF texture files from bytes, streams, or file paths.</summary>
public static class HomeworldLifReader {

  public static HomeworldLifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HomeworldLifFile FromStream(Stream stream) {
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

  public static HomeworldLifFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static HomeworldLifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HomeworldLifFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid LIF file (need at least {HomeworldLifFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != HomeworldLifFile.Magic[0] || data[1] != HomeworldLifFile.Magic[1] || data[2] != HomeworldLifFile.Magic[2] || data[3] != HomeworldLifFile.Magic[3])
      throw new InvalidDataException("Invalid LIF magic bytes.");

    var version = BitConverter.ToInt32(data, 4);
    var width = BitConverter.ToInt32(data, 8);
    var height = BitConverter.ToInt32(data, 12);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid LIF dimensions: {width}x{height}.");

    var pixelDataSize = width * height * 4;
    if (data.Length < HomeworldLifFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("LIF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(HomeworldLifFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      PixelData = pixelData,
    };
  }
}
