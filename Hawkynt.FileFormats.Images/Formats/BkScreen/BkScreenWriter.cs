using System;

namespace FileFormat.BkScreen;

/// <summary>Assembles a BK screen dump from a <see cref="BkScreenFile"/>.</summary>
public static class BkScreenWriter {

  public static byte[] ToBytes(BkScreenFile file) {
    var data = file.Data ?? [];
    var size = BkScreenFile.ScreenSize * file.Frames + (file.IsColor ? file.Frames : 0);
    var result = new byte[size];
    data.AsSpan(0, Math.Min(data.Length, size)).CopyTo(result);

    return result;
  }
}
