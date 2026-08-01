using System;
using System.IO;

namespace FileFormat.BestPaint;

/// <summary>Assembles a Best Paint picture: the bitmap, the cell inks, then the shared background.</summary>
public static class BestPaintWriter {

  public static byte[] ToBytes(BestPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[BestPaintFile.FileSize];
  }

  public static void ToFile(BestPaintFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
