using System;
using System.IO;

namespace FileFormat.Gigacad;

/// <summary>Reads Atari ST GigaCAD monochrome images from bytes, streams, or file paths.</summary>
public static class GigacadReader {

  public static GigacadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GigaCAD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GigacadFile FromStream(Stream stream) {
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

  public static GigacadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != GigacadFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid GigaCAD data size: expected exactly {GigacadFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[GigacadFile.ExpectedFileSize];
    data.AsSpan(0, GigacadFile.ExpectedFileSize).CopyTo(pixelData);

    return new GigacadFile {
      PixelData = pixelData
    };
  }
}
