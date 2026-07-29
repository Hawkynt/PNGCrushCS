using System;

namespace FileFormat.JupiterAce;

/// <summary>Assembles Jupiter Ace character screen file bytes.</summary>
public static class JupiterAceWriter {

  public static byte[] ToBytes(JupiterAceFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[JupiterAceFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, JupiterAceFile.FileSize)).CopyTo(result);
    return result;
  }
}
