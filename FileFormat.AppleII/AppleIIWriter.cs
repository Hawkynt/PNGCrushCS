using System;

namespace FileFormat.AppleII;

/// <summary>Assembles Apple II Hi-Res Graphics file bytes from an <see cref="AppleIIFile"/>.</summary>
public static class AppleIIWriter {

  public static byte[] ToBytes(AppleIIFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return AppleIILayoutConverter.Interleave(file.PixelData, file.Mode);
  }
}
