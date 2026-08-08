using System;
using System.IO;

namespace FileFormat.GunPaint;

/// <summary>Writes a GunPaint picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole because every area of it is at an absolute offset, and the
/// background table is in three pieces that are not next to each other — so there is nothing to
/// reassemble here beyond insisting on the length the offsets assume.
/// </remarks>
public static class GunPaintWriter {

  public static byte[] ToBytes(GunPaintFile file) {
    var data = file.Data;
    if (data == null || data.Length < GunPaintFile.FileSize)
      throw new InvalidDataException($"A GunPaint picture is {GunPaintFile.FileSize} bytes.");

    return data.AsSpan(0, GunPaintFile.FileSize).ToArray();
  }
}
