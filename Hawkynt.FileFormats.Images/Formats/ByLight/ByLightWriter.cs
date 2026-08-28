using System;

namespace FileFormat.ByLight;

/// <summary>Writes the verified byLight record followed by one complete JPEG stream.</summary>
public static class ByLightWriter {

  public static byte[] ToBytes(ByLightFile file) {
    if (file.JpegData == null || file.JpegData.Length < 2 || file.JpegData[0] != 0xFF || file.JpegData[1] != 0xD8)
      throw new ArgumentException("byLight requires a JPEG payload.", nameof(file));

    var output = new byte[checked(ByLightFile.HeaderSize + file.JpegData.Length)];
    if (file.Header is { Length: >= ByLightFile.HeaderSize })
      file.Header.AsSpan(0, ByLightFile.HeaderSize).CopyTo(output);
    output[0] = ByLightFile.Magic[0];
    output[1] = ByLightFile.Magic[1];
    file.JpegData.CopyTo(output, ByLightFile.HeaderSize);
    return output;
  }
}
