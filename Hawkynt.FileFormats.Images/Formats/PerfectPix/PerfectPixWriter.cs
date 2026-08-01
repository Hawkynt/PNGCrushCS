using System;

namespace FileFormat.PerfectPix;

/// <summary>Assembles the Perfect Pix head file.</summary>
/// <remarks>
/// Only the head is a return value: the two fields are separate files, and a picture without them
/// is a size and a palette with nothing to draw. They go out through the companion path instead,
/// which is what keeps the three from being written one at a time and drifting apart.
/// </remarks>
public static class PerfectPixWriter {

  public static byte[] ToBytes(PerfectPixFile file) {
    var head = file.Head ?? [];
    var result = new byte[PerfectPixFile.HeadSize];
    head.AsSpan(0, Math.Min(head.Length, result.Length)).CopyTo(result);
    return result;
  }
}
