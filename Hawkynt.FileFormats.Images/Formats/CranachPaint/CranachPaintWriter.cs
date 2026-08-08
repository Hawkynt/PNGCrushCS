using System;

namespace FileFormat.CranachPaint;

/// <summary>Assembles a TmS Cranach Paint picture from a <see cref="CranachPaintFile"/>.</summary>
public static class CranachPaintWriter {

  /// <summary>Writes the file, which is already whole in memory because every offset is absolute.</summary>
  public static byte[] ToBytes(CranachPaintFile file) => (byte[])(file.Data ?? []).Clone();
}
