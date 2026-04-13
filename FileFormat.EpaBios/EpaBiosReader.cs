using System;
using System.IO;

namespace FileFormat.EpaBios;

/// <summary>Reads Award BIOS Logo (.epa) files from bytes, streams, or file paths.</summary>
public static class EpaBiosReader {

  public static EpaBiosFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EpaBios file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EpaBiosFile FromStream(Stream stream) {
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

  public static EpaBiosFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != EpaBiosFile.FileSize)
      throw new InvalidDataException($"Invalid EpaBios data size: expected exactly {EpaBiosFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[EpaBiosFile.FileSize];
    data.Slice(0, EpaBiosFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static EpaBiosFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != EpaBiosFile.FileSize)
      throw new InvalidDataException($"Invalid EpaBios data size: expected exactly {EpaBiosFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[EpaBiosFile.FileSize];
    data.AsSpan(0, EpaBiosFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
