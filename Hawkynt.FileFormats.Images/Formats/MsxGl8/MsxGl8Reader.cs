using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MsxGl8;

/// <summary>Reads sized-header MSX2 Screen 8 pictures from bytes, streams, or file paths.</summary>
public static class MsxGl8Reader {

  public static MsxGl8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen 8 picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxGl8File FromStream(Stream stream) {
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

  public static MsxGl8File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MsxGl8File.HeaderSize + 1)
      throw new InvalidDataException($"A Screen 8 picture is at least {MsxGl8File.HeaderSize + 1} bytes, got {data.Length}.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    if (width < 1 || height < 1 || width > MsxGl8File.MaxDimension || height > MsxGl8File.MaxDimension)
      throw new InvalidDataException($"Not a Screen 8 picture: the header claims {width}x{height}.");

    // One byte a pixel and nothing else, so the length is exactly determined.
    var expected = MsxGl8File.HeaderSize + width * height;
    if (data.Length != expected)
      throw new InvalidDataException($"A {width}x{height} Screen 8 picture is {expected} bytes, got {data.Length}.");

    var pixels = new byte[width * height];
    data[MsxGl8File.HeaderSize..].CopyTo(pixels);

    return new() { Width = width, Height = height, PixelData = pixels };
  }

  public static MsxGl8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
