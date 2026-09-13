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

  public static GreatPaintFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static GreatPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != GreatPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Great Paint file must be exactly {GreatPaintFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[GreatPaintFile.ExpectedFileSize];
    data.Slice(0, GreatPaintFile.ExpectedFileSize).CopyTo(pixelData);

    return new GreatPaintFile { PixelData = pixelData };
    }

  public static GreatPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != GreatPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Great Paint file must be exactly {GreatPaintFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[GreatPaintFile.ExpectedFileSize];
    data.AsSpan(0, GreatPaintFile.ExpectedFileSize).CopyTo(pixelData);

    return new GreatPaintFile { PixelData = pixelData };
  }
}
