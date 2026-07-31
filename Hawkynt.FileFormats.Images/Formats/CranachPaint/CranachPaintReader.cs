using System;
using System.IO;
using System.Text;

namespace FileFormat.CranachPaint;

/// <summary>Reads TmS Cranach Paint pictures from bytes, streams, or file paths.</summary>
public static class CranachPaintReader {

  public static CranachPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CranachPaintFile FromStream(Stream stream) {
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

  public static CranachPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 812
        || Encoding.ASCII.GetString(data[..CranachPaintFile.Signature.Length]) != CranachPaintFile.Signature
        || data[3] != 0 || data[4] != 3 || data[5] != 44 || data[10] != 0)
      throw new InvalidDataException("Not a TmS Cranach Paint picture.");

    var width = (data[6] << 8) | data[7];
    var height = (data[8] << 8) | data[9];
    var count = width * height;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Cranach picture is not {width}x{height}.");

    var expected = data[11] switch {
      1 => CranachPaintFile.PixelsOffset + ((width + 7) >> 3) * height,
      8 => CranachPaintFile.PixelsOffset + count,
      24 => CranachPaintFile.PixelsOffset + count * 3,
      _ => throw new InvalidDataException($"A Cranach picture is 1, 8 or 24 bits a pixel, not {data[11]}."),
    };

    if (data.Length != expected)
      throw new InvalidDataException($"A {width}x{height} picture at {data[11]} bits is not {data.Length} bytes.");

    return new() { Data = data.ToArray(), Width = width, Height = height, Depth = data[11] };
  }

  public static CranachPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
