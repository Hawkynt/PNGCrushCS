using System;

namespace FileFormat.VdcBitmap;

/// <summary>Assembles VDC BitMap bytes from a <see cref="VdcBitmapFile"/>.</summary>
public static class VdcBitmapWriter {

  /// <summary>Writes the unpacked form, which is version 2.</summary>
  /// <remarks>
  /// Version 3 packs the bitmap against a table of escape bytes it also chooses. Writing version 2
  /// is not a shortcut around that: the two are equally readable and the packed one only ever wins
  /// on size, which is not what this project is deciding here.
  /// </remarks>
  public static byte[] ToBytes(VdcBitmapFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = (file.Width + 7) >> 3;
    var result = new byte[VdcBitmapFile.Version2BitmapOffset + stride * file.Height];

    VdcBitmapFile.Signature.CopyTo(result);
    result[3] = 2;
    result[4] = (byte)(file.Width >> 8);
    result[5] = (byte)file.Width;
    result[6] = (byte)(file.Height >> 8);
    result[7] = (byte)file.Height;

    bitmap
      .AsSpan(0, Math.Min(bitmap.Length, result.Length - VdcBitmapFile.Version2BitmapOffset))
      .CopyTo(result.AsSpan(VdcBitmapFile.Version2BitmapOffset));

    return result;
  }
}
