using System;

namespace FileFormat.AtariPicture;

/// <summary>Assembles APAC picture bytes.</summary>
public static class AtariPictureWriter {

  public static byte[] ToBytes(AtariPictureFile file) {
    var result = new byte[AtariPictureFile.FileSize];
    var data = file.PixelData ?? [];
    data.AsSpan(0, Math.Min(data.Length, AtariPictureFile.FileSize)).CopyTo(result);

    return result;
  }
}
