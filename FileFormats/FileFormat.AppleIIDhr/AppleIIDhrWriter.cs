using System;

namespace FileFormat.AppleIIDhr;

/// <summary>Assembles Apple II Double High-Resolution graphics screen dump bytes from an <see cref="AppleIIDhrFile"/>.</summary>
public static class AppleIIDhrWriter {

  public static byte[] ToBytes(AppleIIDhrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AppleIIDhrFile.FileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, AppleIIDhrFile.FileSize)).CopyTo(result);
    return result;
  }
}
