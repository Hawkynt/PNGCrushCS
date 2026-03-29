using System;
using System.IO;

namespace FileFormat.StTrueColor;

/// <summary>Reads ST True Color files from bytes, streams, or file paths.</summary>
public static class StTrueColorReader {

  public static StTrueColorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ST True Color file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static StTrueColorFile FromStream(Stream stream) {
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

  public static StTrueColorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < StTrueColorFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid ST True Color file; expected {StTrueColorFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[StTrueColorFile.FileSize];
    data.AsSpan(0, StTrueColorFile.FileSize).CopyTo(pixelData.AsSpan(0));

    return new StTrueColorFile {
      Width = 320,
      Height = 200,
      PixelData = pixelData,
    };
  }
}
