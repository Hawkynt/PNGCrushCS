using System;
using System.IO;
using System.Text;

namespace FileFormat.ScitexCt;

/// <summary>Reads Scitex CT files from bytes, streams, or file paths.</summary>
public static class ScitexCtReader {

  public static ScitexCtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Scitex CT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ScitexCtFile FromStream(Stream stream) {
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

  public static ScitexCtFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ScitexCtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ScitexCtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Scitex CT file.");

    var sig = Encoding.ASCII.GetString(data, 0, 2);
    if (sig != "CT")
      throw new InvalidDataException($"Invalid Scitex CT signature: expected 'CT', got '{sig}'.");

    var header = ScitexCtHeader.ReadFrom(data.AsSpan());

    var width = header.Width;
    var height = header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid Scitex CT width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid Scitex CT height: {height}.");

    var channels = header.ColorMode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      ScitexCtColorMode.Cmyk => 4,
      _ => throw new InvalidDataException($"Unknown Scitex CT color mode: {(int)header.ColorMode}.")
    };

    var expectedPixelBytes = width * height * channels;

    if (data.Length < ScitexCtHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {ScitexCtHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(ScitexCtHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new ScitexCtFile {
      Width = width,
      Height = height,
      BitsPerComponent = header.BitsPerComponent,
      ColorMode = header.ColorMode,
      HResolution = header.HResolution,
      VResolution = header.VResolution,
      Description = header.Description,
      PixelData = pixelData
    };
  }
}
