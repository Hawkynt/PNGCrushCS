using System;

namespace FileFormat.NokiaNlm;

/// <summary>Assembles Nokia Logo Manager image file bytes.</summary>
public static class NokiaNlmWriter {

  public static byte[] ToBytes(NokiaNlmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[NokiaNlmFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, NokiaNlmFile.FileSize)).CopyTo(result);
    return result;
  }
}
