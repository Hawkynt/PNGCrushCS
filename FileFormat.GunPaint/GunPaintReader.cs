using System;
using System.IO;

namespace FileFormat.GunPaint;

/// <summary>Reads C64 GunPaint FLI files from bytes, streams, or file paths.</summary>
public static class GunPaintReader {

  public static GunPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GunPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GunPaintFile FromStream(Stream stream) {
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

  public static GunPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < GunPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid GunPaint file (expected {GunPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != GunPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid GunPaint file size (expected {GunPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    // Raw data payload (everything after the load address)
    var rawData = new byte[GunPaintFile.RawDataSize];
    data.Slice(GunPaintFile.LoadAddressSize, GunPaintFile.RawDataSize).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData
    };
    }

  public static GunPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GunPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid GunPaint file (expected {GunPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != GunPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid GunPaint file size (expected {GunPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    // Raw data payload (everything after the load address)
    var rawData = new byte[GunPaintFile.RawDataSize];
    data.AsSpan(GunPaintFile.LoadAddressSize, GunPaintFile.RawDataSize).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData
    };
  }
}
