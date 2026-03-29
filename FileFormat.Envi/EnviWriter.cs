using System;

namespace FileFormat.Envi;

/// <summary>Assembles ENVI file bytes (header + pixel data) from an EnviFile.</summary>
public static class EnviWriter {

  public static byte[] ToBytes(EnviFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var headerBytes = EnviHeaderParser.Format(
      file.Width,
      file.Height,
      file.Bands,
      file.DataType,
      file.Interleave,
      file.ByteOrder
    );

    var result = new byte[headerBytes.Length + file.PixelData.Length];
    headerBytes.AsSpan(0, headerBytes.Length).CopyTo(result.AsSpan(0));

    if (file.PixelData.Length > 0)
      file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(headerBytes.Length));

    return result;
  }
}
