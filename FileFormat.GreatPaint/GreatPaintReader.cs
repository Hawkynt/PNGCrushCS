using System;
using System.IO;

namespace FileFormat.GreatPaint;

/// <summary>Reads Great Paint files from bytes, streams, or file paths.</summary>
public static class GreatPaintReader {

  public static GreatPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Great Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GreatPaintFile FromStream(Stream stream) {
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

  public static GreatPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GreatPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != GreatPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Great Paint file must be exactly {GreatPaintFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[GreatPaintFile.ExpectedFileSize];
    data.AsSpan(0, GreatPaintFile.ExpectedFileSize).CopyTo(pixelData);

    return new GreatPaintFile { PixelData = pixelData };
  }
}
