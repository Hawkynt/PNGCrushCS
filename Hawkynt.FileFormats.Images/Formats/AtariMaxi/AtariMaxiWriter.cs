using System;

namespace FileFormat.AtariMaxi;

/// <summary>Assembles Maxi file bytes from an <see cref="AtariMaxiFile"/>.</summary>
public static class AtariMaxiWriter {

  public static byte[] ToBytes(AtariMaxiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariMaxiFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariMaxiFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
