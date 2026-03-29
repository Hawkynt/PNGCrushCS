using System;
using System.IO;

namespace FileFormat.PixelPerfect;

/// <summary>Reads Pixel Perfect (.pp/.ppp) files from bytes, streams, or file paths.</summary>
public static class PixelPerfectReader {

  public static PixelPerfectFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pixel Perfect file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PixelPerfectFile FromStream(Stream stream) {
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

  public static PixelPerfectFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PixelPerfectFile.LoadAddressSize + PixelPerfectFile.MinBitmapSize)
      throw new InvalidDataException($"Data too small for a valid Pixel Perfect file (expected at least {PixelPerfectFile.LoadAddressSize + PixelPerfectFile.MinBitmapSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - PixelPerfectFile.LoadAddressSize];
    data.AsSpan(PixelPerfectFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
