using System;
using FileFormat.Bmp;

namespace FileFormat.JigsawPicture;

/// <summary>Writes Jigsaw pictures by replacing a BMP file signature with the format's JG signature.</summary>
public static class JigsawPictureWriter {

  public static byte[] ToBytes(JigsawPictureFile file) {
    ArgumentNullException.ThrowIfNull(file.Image);
    var bytes = BmpWriter.ToBytes(BmpFile.FromRawImage(file.Image));
    if (bytes.Length < JigsawPictureFile.MinimumSize)
      throw new InvalidOperationException("The BMP encoder returned a file shorter than a Jigsaw header.");

    bytes[0] = (byte)'J';
    bytes[1] = (byte)'G';
    return bytes;
  }
}
