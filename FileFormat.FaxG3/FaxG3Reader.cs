using System;
using System.IO;

namespace FileFormat.FaxG3;

/// <summary>Reads Raw Group 3 fax image files from bytes, streams, or file paths.</summary>
public static class FaxG3Reader {

  public static FaxG3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FaxG3 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FaxG3File FromStream(Stream stream) {
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

  public static FaxG3File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static FaxG3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FaxG3File.HeaderSize)
      throw new InvalidDataException("Data too small for a valid FaxG3 file.");

    var width = 1728;
    var height = 2200;

    var pixelBytes = (width + 7) / 8 * height;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - FaxG3File.HeaderSize);
    if (available > 0)
      data.AsSpan(FaxG3File.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
