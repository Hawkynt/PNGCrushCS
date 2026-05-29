using System;

namespace FileFormat.CoCoMax;

/// <summary>Assembles CoCoMax paint program bytes from a <see cref="CoCoMaxFile"/>.</summary>
public static class CoCoMaxWriter {

  public static byte[] ToBytes(CoCoMaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CoCoMaxFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, CoCoMaxFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
