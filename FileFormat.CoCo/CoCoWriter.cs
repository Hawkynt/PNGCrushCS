using System;

namespace FileFormat.CoCo;

/// <summary>Assembles TRS-80 CoCo PMODE 4 graphics bytes from a <see cref="CoCoFile"/>.</summary>
public static class CoCoWriter {

  public static byte[] ToBytes(CoCoFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CoCoFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, CoCoFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
