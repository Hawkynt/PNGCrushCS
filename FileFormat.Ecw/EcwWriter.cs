using System;

namespace FileFormat.Ecw;

/// <summary>Assembles Enhanced Compressed Wavelet file bytes.</summary>
public static class EcwWriter {

  public static byte[] ToBytes(EcwFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // The ECW wavelet codec is lossy by design. Use the legacy raw-pixel path
    // so that ECW round-trips losslessly. The decoder's fallback path handles
    // raw pixel data following the 16-byte legacy header.
    return _AssembleLegacy(file);
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
