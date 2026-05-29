using System;

namespace FileFormat.AtariPicture;

/// <summary>Assembles Atari Picture bytes from an <see cref="AtariPictureFile"/>.</summary>
public static class AtariPictureWriter {

  public static byte[] ToBytes(AtariPictureFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariPictureFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariPictureFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
