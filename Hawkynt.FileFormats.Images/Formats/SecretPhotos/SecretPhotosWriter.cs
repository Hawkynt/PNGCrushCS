using System;

namespace FileFormat.SecretPhotos;

/// <summary>Writes SecretPhotos puzzles with a JPEG at the format's fixed payload offset.</summary>
public static class SecretPhotosWriter {

  public static byte[] ToBytes(SecretPhotosFile file) {
    if (file.Embedded == null || file.Embedded.Length < 3 || file.Embedded[0] != 0xFF || file.Embedded[1] != 0xD8 || file.Embedded[2] != 0xFF)
      throw new ArgumentException("SecretPhotos requires a complete JPEG payload.", nameof(file));

    var output = new byte[checked(SecretPhotosFile.PictureOffset + file.Embedded.Length)];
    SecretPhotosFile.Magic.CopyTo(output);
    file.Embedded.CopyTo(output, SecretPhotosFile.PictureOffset);
    return output;
  }
}
