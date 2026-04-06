using System;
using System.IO;

namespace FileFormat.GigaPaint;

/// <summary>Reads GigaPaint (.gih/.gig) files from bytes, streams, or file paths.</summary>
public static class GigaPaintReader {

  public static GigaPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GigaPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GigaPaintFile FromStream(Stream stream) {
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

  public static GigaPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GigaPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GigaPaintFile.LoadAddressSize + GigaPaintFile.MinBitmapSize)
      throw new InvalidDataException($"Data too small for a valid GigaPaint file (expected at least {GigaPaintFile.LoadAddressSize + GigaPaintFile.MinBitmapSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - GigaPaintFile.LoadAddressSize];
    data.AsSpan(GigaPaintFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
