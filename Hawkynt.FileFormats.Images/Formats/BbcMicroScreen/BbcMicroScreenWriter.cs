using System;

namespace FileFormat.BbcMicroScreen;

/// <summary>Assembles BBC Micro screen dump bytes.</summary>
public static class BbcMicroScreenWriter {

  public static byte[] ToBytes(BbcMicroScreenFile file) {
    var size = BbcMicroScreenFile.FileSizeFor(file.Mode);
    var result = new byte[size];

    var data = file.ScreenData ?? [];
    data.AsSpan(0, Math.Min(data.Length, size)).CopyTo(result);

    return result;
  }
}
