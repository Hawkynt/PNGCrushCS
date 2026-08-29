using System;

namespace FileFormat.DispThumbnail;

/// <summary>Writes colour DISPTNL5 thumbnails with a complete JPEG payload at offset 168.</summary>
public static class DispThumbnailWriter {

  public static byte[] ToBytes(DispThumbnailFile file) {
    if (file.Embedded == null || file.Embedded.Length < 3 || file.Embedded[0] != 0xFF || file.Embedded[1] != 0xD8 || file.Embedded[2] != 0xFF)
      throw new ArgumentException("DISPTNL5 requires a complete JPEG payload.", nameof(file));

    var output = new byte[checked(DispThumbnailFile.PictureOffset + file.Embedded.Length)];
    DispThumbnailFile.Magic.CopyTo(output);
    output[DispThumbnailFile.Magic.Length] = DispThumbnailFile.JpegMarker;
    file.Embedded.CopyTo(output, DispThumbnailFile.PictureOffset);
    return output;
  }
}
