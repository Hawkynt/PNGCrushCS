using System;
using System.IO;

namespace FileFormat.CommodorePet;

/// <summary>Parses commodore pet petscii screen dump from raw bytes.</summary>
public static class CommodorePetReader {

  public static CommodorePetFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CommodorePetFile FromStream(Stream stream) {
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

  public static CommodorePetFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CommodorePetFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CommodorePetFile.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected 1000.");

    var pixelData = new byte[CommodorePetFile.ImageWidth * CommodorePetFile.ImageHeight];
    data.AsSpan(0, pixelData.Length).CopyTo(pixelData.AsSpan(0));

    return new CommodorePetFile { PixelData = pixelData };
  }
}
