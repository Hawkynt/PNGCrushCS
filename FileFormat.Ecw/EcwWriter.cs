using System;
using FileFormat.Ecw.Codec;

namespace FileFormat.Ecw;

/// <summary>Assembles Enhanced Compressed Wavelet file bytes.</summary>
public static class EcwWriter {

  public static byte[] ToBytes(EcwFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.PixelData.Length == 0)
      return _AssembleLegacy(file);

    // Use the ECW codec encoder for proper wavelet compression
    try {
      return EcwEncoder.Encode(file.PixelData, file.Width, file.Height);
    } catch {
      return _AssembleLegacy(file);
    }
  }

  /// <summary>Fallback legacy writer: simple header + raw pixel data.</summary>
  private static byte[] _AssembleLegacy(EcwFile file) {
    var pixelBytes = file.PixelData.Length;
    var fileSize = EcwFile.HeaderSize + pixelBytes;
    var result = new byte[fileSize];

    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    result[4] = (byte)(file.Height & 0xFF);
    result[5] = (byte)((file.Height >> 8) & 0xFF);

    file.PixelData.AsSpan(0, Math.Min(pixelBytes, file.PixelData.Length)).CopyTo(result.AsSpan(EcwFile.HeaderSize));
    return result;
  }
}
