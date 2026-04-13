using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.QuantumPaint;

/// <summary>Reads Atari ST QuantumPaint files from bytes, streams, or file paths.</summary>
public static class QuantumPaintReader {

  public static QuantumPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QuantumPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QuantumPaintFile FromStream(Stream stream) {
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

  public static QuantumPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < QuantumPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid QuantumPaint file: expected at least {QuantumPaintFile.MinFileSize} bytes, got {data.Length}.");

    var span = data;

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span.Slice(i * 2, 2));

    var pixelData = new byte[QuantumPaintFile.PixelDataSize];
    span.Slice(QuantumPaintFile.PaletteSize, QuantumPaintFile.PixelDataSize).CopyTo(pixelData);

    return new QuantumPaintFile {
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static QuantumPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < QuantumPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid QuantumPaint file: expected at least {QuantumPaintFile.MinFileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span.Slice(i * 2, 2));

    var pixelData = new byte[QuantumPaintFile.PixelDataSize];
    span.Slice(QuantumPaintFile.PaletteSize, QuantumPaintFile.PixelDataSize).CopyTo(pixelData);

    return new QuantumPaintFile {
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
