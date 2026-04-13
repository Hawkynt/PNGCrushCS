using System;
using System.IO;

namespace FileFormat.PabloPaint;

/// <summary>Reads Atari ST Pablo Paint files from bytes, streams, or file paths.</summary>
public static class PabloPaintReader {

  public static PabloPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pablo Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PabloPaintFile FromStream(Stream stream) {
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

  public static PabloPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PabloPaintFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Pablo Paint file: expected at least {PabloPaintFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[PabloPaintFile.FileSize];
    data.Slice(0, PabloPaintFile.FileSize).CopyTo(pixelData);

    return new PabloPaintFile { PixelData = pixelData };
    }

  public static PabloPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PabloPaintFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Pablo Paint file: expected at least {PabloPaintFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[PabloPaintFile.FileSize];
    data.AsSpan(0, PabloPaintFile.FileSize).CopyTo(pixelData);

    return new PabloPaintFile { PixelData = pixelData };
  }
}
