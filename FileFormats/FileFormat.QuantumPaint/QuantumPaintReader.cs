using System;
using System.IO;

namespace FileFormat.QuantumPaint;

/// <summary>Reads Atari ST QuantumPaint files from bytes, streams, or file paths.</summary>
public static class QuantumPaintReader {

  public static QuantumPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QuantumPaint file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static QuantumPaintFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static QuantumPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < QuantumPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid QuantumPaint file: expected at least {QuantumPaintFile.MinFileSize} bytes, got {data.Length}.");

    var header = QuantumPaintHeader.ReadFrom(data);

    return new QuantumPaintFile {
      Palette = header.Palette,
      PixelData = data.Slice(QuantumPaintFile.PaletteSize, QuantumPaintFile.PixelDataSize).ToArray(),
    };
  }

  public static QuantumPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
