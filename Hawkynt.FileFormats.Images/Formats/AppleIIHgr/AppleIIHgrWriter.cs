using System;

namespace FileFormat.AppleIIHgr;

/// <summary>Assembles Apple II High-Resolution graphics screen dump bytes from an <see cref="AppleIIHgrFile"/>.</summary>
public static class AppleIIHgrWriter {

  public static byte[] ToBytes(AppleIIHgrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AppleIIHgrFile.FileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, AppleIIHgrFile.FileSize)).CopyTo(result);
    return result;
  }
}
