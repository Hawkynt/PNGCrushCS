using System;
using System.IO;
using System.Text;

namespace FileFormat.InShape;

/// <summary>Reads InShape pictures from bytes, streams, or file paths.</summary>
public static class InShapeReader {

  public static InShapeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InShapeFile FromStream(Stream stream) {
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

  public static InShapeFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 17 || Encoding.ASCII.GetString(data[..InShapeFile.Signature.Length]) != InShapeFile.Signature
        || data[8] != 0)
      throw new InvalidDataException("Not an InShape picture.");

    var width = (data[12] << 8) | data[13];
    var height = (data[14] << 8) | data[15];
    var count = width * height;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"An InShape picture is not {width}x{height}.");

    var expected = data[9] switch {
      InShapeFile.MonochromeMode => InShapeFile.PixelsOffset + ((width + 7) >> 3) * height,
      InShapeFile.GrayscaleMode => InShapeFile.PixelsOffset + count,
      InShapeFile.TrueColorMode => InShapeFile.PixelsOffset + count * 3,
      InShapeFile.PaddedTrueColorMode => InShapeFile.PixelsOffset + count * 4,
      _ => throw new InvalidDataException($"An InShape picture is mode 0, 1, 4 or 5, not {data[9]}."),
    };

    if (data.Length != expected)
      throw new InvalidDataException($"A {width}x{height} picture of mode {data[9]} is not {data.Length} bytes.");

    return new() { Data = data.ToArray(), Width = width, Height = height, Mode = data[9] };
  }

  public static InShapeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
