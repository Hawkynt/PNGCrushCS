using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MsxGl6;

/// <summary>Reads MSX2 GL6 pictures from bytes, streams, or file paths.</summary>
public static class MsxGl6Reader {

  public static MsxGl6File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GL6 picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxGl6File FromStream(Stream stream) {
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

  public static MsxGl6File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MsxGl6File.HeaderSize + 1)
      throw new InvalidDataException($"A GL6 picture is at least {MsxGl6File.HeaderSize + 1} bytes, got {data.Length}.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    if (width < 1 || height < 1 || width > MsxGl6File.MaxDimension || height > MsxGl6File.MaxDimension)
      throw new InvalidDataException($"Not a GL6 picture: the header claims {width}x{height}.");

    var size = MsxGl6File.PixelDataSizeFor(width, height);
    if (data.Length < MsxGl6File.HeaderSize + size)
      throw new InvalidDataException($"A {width}x{height} GL6 picture needs {MsxGl6File.HeaderSize + size} bytes, got {data.Length}.");

    var pixels = new byte[size];
    data.Slice(MsxGl6File.HeaderSize, size).CopyTo(pixels);

    return new() { Width = width, Height = height, PixelData = pixels, Palette = [] };
  }

  public static MsxGl6File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
