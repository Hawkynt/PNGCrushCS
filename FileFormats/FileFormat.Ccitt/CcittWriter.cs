using System;

namespace FileFormat.Ccitt;

/// <summary>Encodes 1bpp pixel data to CCITT-compressed bytes.</summary>
public static class CcittWriter {

  public static byte[] ToBytes(CcittFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Format switch {
      CcittFormat.Group3_1D => CcittG3Encoder.Encode(file.PixelData, file.Width, file.Height),
      CcittFormat.Group4 => CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height),
      _ => throw new NotSupportedException($"CCITT format {file.Format} is not supported.")
    };
  }
}
