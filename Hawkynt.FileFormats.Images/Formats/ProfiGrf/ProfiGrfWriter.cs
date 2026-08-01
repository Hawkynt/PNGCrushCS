using System;

namespace FileFormat.ProfiGrf;

/// <summary>Assembles Profi GRF picture bytes from a <see cref="ProfiGrfFile"/>.</summary>
public static class ProfiGrfWriter {

  public static byte[] ToBytes(ProfiGrfFile file) {
    var data = file.Data ?? [];
    var result = new byte[ProfiGrfFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    // The ten leading bytes are what a reader identifies the format by, so they are written
    // whether or not the picture came from a file that had them.
    ProfiGrfFile.Signature.CopyTo(result);

    return result;
  }
}
