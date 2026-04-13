using System;
using System.IO;

namespace FileFormat.NokiaLogo;

/// <summary>Reads Nokia Operator Logo files from bytes, streams, or file paths.</summary>
public static class NokiaLogoReader {

  public static NokiaLogoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NokiaLogo file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaLogoFile FromStream(Stream stream) {
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

  public static NokiaLogoFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != NokiaLogoFile.FileSize)
      throw new InvalidDataException($"Invalid NokiaLogo data size: expected exactly {NokiaLogoFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[NokiaLogoFile.FileSize];
    data.Slice(0, NokiaLogoFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static NokiaLogoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != NokiaLogoFile.FileSize)
      throw new InvalidDataException($"Invalid NokiaLogo data size: expected exactly {NokiaLogoFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[NokiaLogoFile.FileSize];
    data.AsSpan(0, NokiaLogoFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
