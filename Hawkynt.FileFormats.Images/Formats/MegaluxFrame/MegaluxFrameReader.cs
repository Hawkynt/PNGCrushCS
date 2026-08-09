using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MegaluxFrame;

/// <summary>Reads Megalux Frame pictures from bytes, streams, or file paths.</summary>
public static class MegaluxFrameReader {

  public static MegaluxFrameFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Megalux Frame file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MegaluxFrameFile FromStream(Stream stream) {
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

  public static MegaluxFrameFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MegaluxFrameFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MegaluxFrameFile.PixelDataOffset)
      throw new InvalidDataException(
        $"Data too small for a Megalux Frame (minimum {MegaluxFrameFile.PixelDataOffset} bytes, got {data.Length}).");

    if (!data[..MegaluxFrameFile.Signature.Length].SequenceEqual(MegaluxFrameFile.Signature))
      throw new InvalidDataException("Not a Megalux Frame: it does not begin with FRM.");

    var code = data[3];
    if (code != MegaluxFrameFile.SupportedFormatCode)
      throw new InvalidDataException(
        $"A Megalux Frame states pixel layout {code}; only layout {MegaluxFrameFile.SupportedFormatCode}, "
        + "four bytes a pixel with blue first, is read here.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    if (width < 1 || height < 1)
      throw new InvalidDataException($"A Megalux Frame states a picture of {width}x{height}.");

    var needed = (long)width * height * MegaluxFrameFile.BytesPerPixel + MegaluxFrameFile.PixelDataOffset;
    if (data.Length < needed)
      throw new InvalidDataException(
        $"A Megalux Frame of {width}x{height} needs {needed} bytes and the file holds {data.Length}.");

    var pixels = new byte[width * height * 3];
    var from = MegaluxFrameFile.PixelDataOffset;
    var to = 0;
    for (var i = 0; i < width * height; ++i, from += MegaluxFrameFile.BytesPerPixel, to += 3) {
      pixels[to] = data[from + 2];
      pixels[to + 1] = data[from + 1];
      pixels[to + 2] = data[from];
    }

    return new() { Width = width, Height = height, PixelData = pixels };
  }
}
