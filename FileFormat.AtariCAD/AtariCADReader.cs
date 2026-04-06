using System;
using System.IO;

namespace FileFormat.AtariCAD;

/// <summary>Reads Atari CAD Screen files from bytes, streams, or file paths.</summary>
public static class AtariCADReader {

  public static AtariCADFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari CAD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariCADFile FromStream(Stream stream) {
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

  public static AtariCADFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AtariCADFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariCADFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Atari CAD data size: expected exactly {AtariCADFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariCADFile.ExpectedFileSize];
    data.AsSpan(0, AtariCADFile.ExpectedFileSize).CopyTo(pixelData);

    return new AtariCADFile { PixelData = pixelData };
  }
}
