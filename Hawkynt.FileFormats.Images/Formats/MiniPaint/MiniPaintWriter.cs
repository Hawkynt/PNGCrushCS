using System;
using System.IO;

namespace FileFormat.MiniPaint;

/// <summary>Assembles a Mini Paint picture: signature, control bytes, bitmap, then the colour areas.</summary>
public static class MiniPaintWriter {

  public static byte[] ToBytes(MiniPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[MiniPaintFile.FileSize];
  }

  public static void ToFile(MiniPaintFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
