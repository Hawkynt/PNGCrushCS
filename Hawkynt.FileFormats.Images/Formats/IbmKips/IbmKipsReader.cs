using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.IbmKips;

/// <summary>Reads IBM KIPS pictures from bytes, streams, or file paths.</summary>
public static class IbmKipsReader {

  public static IbmKipsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("KIPS picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IbmKipsFile FromStream(Stream stream) {
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

  public static IbmKipsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IbmKipsFile.HeaderSize || !data[..IbmKipsFile.Magic.Length].SequenceEqual(IbmKipsFile.Magic))
      throw new InvalidDataException("Not a KIPS picture: it does not open with DFIMAG00.");

    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[IbmKipsFile.HeightAt..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[IbmKipsFile.WidthAt..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid KIPS size: {width}x{height}.");

    var needed = IbmKipsFile.HeaderSize + width * height;
    if (data.Length < needed)
      throw new InvalidDataException($"A {width}x{height} KIPS picture needs {needed} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      Header = data[..IbmKipsFile.HeaderSize].ToArray(),
      PixelData = data.Slice(IbmKipsFile.HeaderSize, width * height).ToArray(),
    };
  }

  public static IbmKipsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
