using System;

namespace FileFormat.GreatPaint;

/// <summary>Assembles Great Paint file bytes from a <see cref="GreatPaintFile"/>.</summary>
public static class GreatPaintWriter {

  public static byte[] ToBytes(GreatPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GreatPaintFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GreatPaintFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
