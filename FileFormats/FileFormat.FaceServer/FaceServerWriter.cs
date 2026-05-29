using System;

namespace FileFormat.FaceServer;

/// <summary>Assembles FaceServer file bytes from a <see cref="FaceServerFile"/>.</summary>
public static class FaceServerWriter {

  public static byte[] ToBytes(FaceServerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FaceServerFile.PixelCount];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, FaceServerFile.PixelCount)).CopyTo(result);
    return result;
  }
}
