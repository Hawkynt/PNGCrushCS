using System;
using System.IO;

namespace FileFormat.GunPaint;

/// <summary>Reads GunPaint pictures from bytes, streams, or file paths.</summary>
public static class GunPaintReader {

  public static GunPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GunPaint picture not found.", file.FullName);

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
    // Some files carry one trailing byte the picture does not use.
    if (data.Length != GunPaintFile.FileSize && data.Length != GunPaintFile.FileSize + 1)
      throw new InvalidDataException($"A GunPaint picture is {GunPaintFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static GunPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
