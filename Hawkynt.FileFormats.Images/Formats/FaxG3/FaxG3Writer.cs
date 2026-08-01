using System;

namespace FileFormat.FaxG3;

/// <summary>Assembles Raw Group 3 fax image file bytes.</summary>
public static class FaxG3Writer {

  /// <summary>Codes a picture as a bare Group 3 stream.</summary>
  /// <remarks>
  /// There is no header: a raw fax file is the coding and nothing else, which is why its width has
  /// to be assumed on the way back in. This used to write a six-byte size header of its own and then
  /// the uncompressed rows, which is a private format wearing the extension of a public one.
  /// </remarks>
  public static byte[] ToBytes(FaxG3File file) {
    ArgumentNullException.ThrowIfNull(file);
    return FileFormat.Ccitt.CcittG3Encoder.Encode(file.PixelData, file.Width, file.Height);
  }
}
