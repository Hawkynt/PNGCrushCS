using System;
using System.IO;

namespace FileFormat.SamCoupeScreen;

/// <summary>Assembles SAM Coupe mode 1, 2 and 3 screen (.ss1, .ss2, .ss3) file bytes.</summary>
/// <remarks>
/// Where the interrupt list starts is what fixes the mode, and where it ends must be the end of the
/// file — a screen with anything after its terminator is not one, and one without a terminator runs
/// off the end looking for it.
/// </remarks>
public static class SamCoupeScreenWriter {

  public static byte[] ToBytes(SamCoupeScreenFile file) {
    var data = file.Data ?? [];
    var offset = SamCoupeScreenFile.InterruptOffsetFor(file.Mode);

    if (data.Length <= offset)
      throw new InvalidDataException(
        $"A mode {(int)file.Mode} screen keeps its interrupt list at {offset}; {data.Length} bytes reach no further.");

    while (data[offset] != SamCoupeScreenFile.InterruptTerminator) {
      offset += SamCoupeScreenFile.InterruptRecordSize;
      if (offset >= data.Length)
        throw new InvalidDataException($"A mode {(int)file.Mode} screen's interrupt list does not terminate.");
    }

    if (offset + 1 != data.Length)
      throw new InvalidDataException(
        $"A mode {(int)file.Mode} screen ends with its interrupt list, not {data.Length - offset - 1} bytes later.");

    return (byte[])data.Clone();
  }
}
