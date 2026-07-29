using System;

namespace FileFormat.Mamut;

/// <summary>Assembles Mamut (.bkg) file bytes.</summary>
public static class MamutWriter {

  public static byte[] ToBytes(MamutFile file) {
    var result = new byte[MamutFile.FileSize];

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, MamutFile.BitmapDataSize)).CopyTo(result);

    return result;
  }
}
