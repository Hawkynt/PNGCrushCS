using System;
using System.IO;

namespace FileFormat.Deluxe;

/// <summary>Reads Deluxe Paint ST files from bytes, streams, or file paths.</summary>
public static class DeluxeReader {

  public static DeluxeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Deluxe Paint ST file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DeluxeFile FromStream(Stream stream) {
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

  public static DeluxeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DeluxeHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Deluxe Paint ST file.");

    if (data.Length < DeluxeFile.FileSize)
      throw new InvalidDataException($"Data too small for the expected {DeluxeFile.FileSize}-byte Deluxe Paint ST file.");

    var span = data.AsSpan();
    var header = DeluxeHeader.ReadFrom(span);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(DeluxeHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new DeluxeFile {
      Width = 320,
      Height = 200,
      Resolution = (ushort)header.Resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
