using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.EggPaint;

/// <summary>Reads Commodore 64 Egg Paint files from bytes, streams, or file paths.</summary>
public static class EggPaintReader {

  public static EggPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Egg Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EggPaintFile FromStream(Stream stream) {
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

  public static EggPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < EggPaintFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid TruePaint picture (got {data.Length} bytes).");

    if (!data[..EggPaintFile.Magic.Length].SequenceEqual(EggPaintFile.Magic))
      throw new InvalidDataException("Not a TruePaint picture: it does not open with TRUP.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"A TruePaint picture of {width}x{height} is no size.");

    var wanted = width * height * 2;
    if (data.Length < EggPaintFile.HeaderSize + wanted)
      throw new InvalidDataException(
        $"{width}x{height} in sixteen-bit colour needs {EggPaintFile.HeaderSize + wanted} bytes; this file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = data.Slice(EggPaintFile.HeaderSize, wanted).ToArray(),
    };
  }

  public static EggPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
