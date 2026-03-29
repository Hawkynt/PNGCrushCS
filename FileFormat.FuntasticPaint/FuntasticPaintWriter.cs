using System;

namespace FileFormat.FuntasticPaint;

/// <summary>Assembles Fun*tastic Paint file bytes from a <see cref="FuntasticPaintFile"/>.</summary>
public static class FuntasticPaintWriter {

  public static byte[] ToBytes(FuntasticPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FuntasticPaintFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, FuntasticPaintFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
