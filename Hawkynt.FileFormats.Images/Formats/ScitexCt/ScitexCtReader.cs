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

  public static ScitexCtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ScitexCtFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ScitexCtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Scitex CT file.");

    if (!ScitexCtHeader.IsContinuousTone(data))
      throw new InvalidDataException("Not a Scitex continuous-tone file: no CT at offset 80.");

    var (width, height, mode) = ScitexCtHeader.Read(data);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A Scitex CT states no size: {width}x{height}.");

    var channels = mode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      _ => 4,
    };

    var expected = width * height * channels;
    if (data.Length < ScitexCtHeader.StructSize + expected)
      throw new InvalidDataException(
        $"{width}x{height} in {channels} separations needs {ScitexCtHeader.StructSize + expected} bytes; this file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      BitsPerComponent = 8,
      ColorMode = mode,
      HResolution = 300,
      VResolution = 300,
      Description = string.Empty,
      PixelData = data.Slice(ScitexCtHeader.StructSize, expected).ToArray(),
    };
  }
}
