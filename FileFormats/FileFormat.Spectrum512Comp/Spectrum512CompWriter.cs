using System;

namespace FileFormat.Spectrum512Comp;

/// <summary>Assembles Spectrum 512 Compressed (SPC) file bytes from a Spectrum512CompFile.</summary>
public static class Spectrum512CompWriter {

  public static byte[] ToBytes(Spectrum512CompFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
