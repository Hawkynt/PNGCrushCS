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

  public static QuantumPaintFile FromStream(Stream stream) => FromSpan(StreamBytes.ReadAll(stream));

  public static QuantumPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < QuantumPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid QuantumPaint file: expected at least {QuantumPaintFile.MinFileSize} bytes, got {data.Length}.");

    var header = QuantumPaintHeader.ReadFrom(data[QuantumPaintFile.PaletteOffset..]);

    return new QuantumPaintFile {
      Palette = header.Palette,
      PixelData = data.Slice(QuantumPaintFile.PixelDataOffset, QuantumPaintFile.PixelDataSize).ToArray(),
    };
  }

  public static QuantumPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
