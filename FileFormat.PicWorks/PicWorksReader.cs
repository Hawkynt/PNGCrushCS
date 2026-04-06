using System;
using System.IO;

namespace FileFormat.PicWorks;

/// <summary>Reads PicWorks files from bytes, streams, or file paths.</summary>
public static class PicWorksReader {

  public static PicWorksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PicWorks file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PicWorksFile FromStream(Stream stream) {
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

  public static PicWorksFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PicWorksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PicWorksHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid PicWorks file.");

    if (data.Length < PicWorksFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {PicWorksFile.FileSize}-byte PicWorks file.");

    var span = data.AsSpan();
    var header = PicWorksHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(PicWorksHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new PicWorksFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
