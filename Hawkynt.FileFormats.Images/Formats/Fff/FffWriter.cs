using System;

namespace FileFormat.Fff;

/// <summary>Writes MAGGI Hairstyles &amp; Cosmetics records with a JPEG portrait at the verified offset.</summary>
public static class FffWriter {

  public static byte[] ToBytes(FffFile file) {
    if (file.PictureData == null || file.PictureData.Length < 3
        || file.PictureData[0] != 0xFF || file.PictureData[1] != 0xD8 || file.PictureData[2] != 0xFF)
      throw new ArgumentException("MAGGI FFF requires a complete JPEG portrait.", nameof(file));

    var output = new byte[checked(FffFile.PictureOffset + file.PictureData.Length)];
    FffFile.Magic.CopyTo(output.AsSpan(FffFile.SignatureOffset, FffFile.SignatureSize));
    file.PictureData.CopyTo(output, FffFile.PictureOffset);
    return output;
  }
}
