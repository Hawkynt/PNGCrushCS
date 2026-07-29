using System;

namespace FileFormat.MadDesigner;

/// <summary>Assembles Mad Designer picture bytes.</summary>
public static class MadDesignerWriter {

  public static byte[] ToBytes(MadDesignerFile file) {
    var result = new byte[MadDesignerFile.FileSize];
    var data = file.BitmapData ?? [];
    data.AsSpan(0, Math.Min(data.Length, MadDesignerFile.FileSize)).CopyTo(result);

    return result;
  }
}
