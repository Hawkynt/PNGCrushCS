using System;

namespace FileFormat.ImageLabBw;

/// <summary>Assembles ImageLab greyscale picture bytes.</summary>
public static class ImageLabBwWriter {

  public static byte[] ToBytes(ImageLabBwFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[ImageLabBwFile.HeaderSize + file.Width * file.Height];

    ImageLabBwFile.Magic.CopyTo(result);
    result[6] = (byte)(file.Width >> 8);
    result[7] = (byte)file.Width;
    result[8] = (byte)(file.Height >> 8);
    result[9] = (byte)file.Height;
    pixels.AsSpan(0, Math.Min(pixels.Length, result.Length - ImageLabBwFile.HeaderSize))
      .CopyTo(result.AsSpan(ImageLabBwFile.HeaderSize));

    return result;
  }
}
