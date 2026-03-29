using System;

namespace FileFormat.Spectrum512Smoosh;

/// <summary>Assembles Spectrum 512 Smooshed (SPS) file bytes from a Spectrum512SmooshFile.</summary>
public static class Spectrum512SmooshWriter {

  public static byte[] ToBytes(Spectrum512SmooshFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
