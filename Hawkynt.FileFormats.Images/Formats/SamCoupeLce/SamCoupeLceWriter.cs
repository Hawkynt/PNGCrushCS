using System;
using System.IO;

namespace FileFormat.SamCoupeLce;

/// <summary>Assembles SAM Coupe interlaced picture (.lce) file bytes.</summary>
/// <remarks>
/// Nothing in the file states either screen's length: the first screen's interrupt list says where
/// it ends and that is where the second begins, and the second must end exactly where the file does.
/// </remarks>
public static class SamCoupeLceWriter {

  public static byte[] ToBytes(SamCoupeLceFile file) {
    var data = file.Data ?? [];
    var second = file.SecondScreenOffset;

    if (second < SamCoupeLceFile.ScreenSize
        || data.Length <= second + SamCoupeLceFile.InterruptOffset
        || data[second - 1] != SamCoupeLceFile.InterruptTerminator
        || data[^1] != SamCoupeLceFile.InterruptTerminator)
      throw new InvalidDataException(
        $"An interlaced picture is two screens each closing its own interrupt list; {data.Length} bytes with the second at {second} are not.");

    return (byte[])data.Clone();
  }
}
