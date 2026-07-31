using System;

namespace FileFormat.Pc98Ebd;

/// <summary>Assembles PC-98 EBD picture bytes from a <see cref="Pc98EbdFile"/>.</summary>
public static class Pc98EbdWriter {

  public static byte[] ToBytes(Pc98EbdFile file) {
    var data = file.Data ?? [];
    var size = Pc98EbdFile.BitmapOffset + file.Height * Pc98EbdFile.Stride;
    var result = new byte[size];
    data.AsSpan(0, Math.Min(data.Length, size)).CopyTo(result);

    return result;
  }
}
