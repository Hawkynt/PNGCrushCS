using System;

namespace FileFormat.PabloPaint;

/// <summary>Assembles Atari ST Pablo Paint file bytes from a <see cref="PabloPaintFile"/>.</summary>
public static class PabloPaintWriter {

  public static byte[] ToBytes(PabloPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PabloPaintFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, PabloPaintFile.FileSize)).CopyTo(result);
    return result;
  }
}
