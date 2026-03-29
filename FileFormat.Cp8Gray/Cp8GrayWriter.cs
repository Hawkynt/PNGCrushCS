using System;

namespace FileFormat.Cp8Gray;

/// <summary>Assembles CP8 grayscale file bytes from a <see cref="Cp8GrayFile"/>.</summary>
public static class Cp8GrayWriter {

  public static byte[] ToBytes(Cp8GrayFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.Width * file.Height];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, result.Length)).CopyTo(result);
    return result;
  }
}
