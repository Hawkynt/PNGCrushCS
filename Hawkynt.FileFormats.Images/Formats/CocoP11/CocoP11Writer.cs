using System;

namespace FileFormat.CocoP11;

/// <summary>Assembles a CoCo P11 picture from a <see cref="CocoP11File"/>.</summary>
public static class CocoP11Writer {

  public static byte[] ToBytes(CocoP11File file) {
    var data = file.Data ?? [];
    var result = new byte[CocoP11File.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    // The header is what a reader identifies the format by, so it is written whether or not the
    // picture came from a file that had one.
    CocoP11File.WriteHeader(result);

    return result;
  }
}
