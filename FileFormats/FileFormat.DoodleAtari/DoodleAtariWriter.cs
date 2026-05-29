using System;

namespace FileFormat.DoodleAtari;

/// <summary>Assembles Atari ST Doodle monochrome image bytes from a DoodleAtariFile.</summary>
public static class DoodleAtariWriter {

  public static byte[] ToBytes(DoodleAtariFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DoodleAtariFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DoodleAtariFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
