using System;
using System.IO;

namespace FileFormat.FuntasticPaint;

/// <summary>Reads Fun*tastic Paint files from bytes, streams, or file paths.</summary>
public static class FuntasticPaintReader {

  public static FuntasticPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fun*tastic Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FuntasticPaintFile FromStream(Stream stream) {
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

  public static FuntasticPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FuntasticPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Fun*tastic Paint file must be exactly {FuntasticPaintFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[FuntasticPaintFile.ExpectedFileSize];
    data.AsSpan(0, FuntasticPaintFile.ExpectedFileSize).CopyTo(pixelData);

    return new FuntasticPaintFile { PixelData = pixelData };
  }
}
