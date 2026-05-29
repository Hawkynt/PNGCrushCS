using System;
using System.IO;

namespace FileFormat.Electronika;

/// <summary>Reads Electronika BK screen dump files from bytes, streams, or file paths.</summary>
public static class ElectronikaReader {

  public static ElectronikaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Electronika file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ElectronikaFile FromStream(Stream stream) {
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

  public static ElectronikaFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != ElectronikaFile.FileSize)
      throw new InvalidDataException($"Invalid Electronika data size: expected exactly {ElectronikaFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[ElectronikaFile.FileSize];
    data.Slice(0, ElectronikaFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static ElectronikaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != ElectronikaFile.FileSize)
      throw new InvalidDataException($"Invalid Electronika data size: expected exactly {ElectronikaFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[ElectronikaFile.FileSize];
    data.AsSpan(0, ElectronikaFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
