using System;

namespace FileFormat.Fuckpaint;

/// <summary>Assembles a Fuckpaint picture from a <see cref="FuckpaintFile"/>.</summary>
public static class FuckpaintWriter {

  /// <summary>Writes the file, which is a fixed length because every area sits at an absolute offset.</summary>
  public static byte[] ToBytes(FuckpaintFile file) {
    var data = new byte[FuckpaintFile.FileSize];
    var source = file.Data ?? [];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    return data;
  }
}
