using System;
using System.IO;

namespace FileFormat.SamCoupeSsx;

/// <summary>Assembles SAM Coupe screen dump (.ssx) file bytes.</summary>
/// <remarks>
/// Nothing in a dump names its mode, so the length has to be exactly one of the five the reader
/// knows — a byte either way and the file is not a screen at all, or is read as a different one.
/// </remarks>
public static class SamCoupeSsxWriter {

  public static byte[] ToBytes(SamCoupeSsxFile file) {
    var data = file.Data ?? [];

    switch (data.Length) {
      case SamCoupeSsxFile.Mode1Size:
      case SamCoupeSsxFile.Mode2Size:
      case SamCoupeSsxFile.Mode3Size:
      case SamCoupeSsxFile.Mode4Size:
      case SamCoupeSsxFile.ChunkySize:
        return (byte[])data.Clone();
      default:
        throw new InvalidDataException(
          $"A SAM Coupe dump is the length of one of its modes; {data.Length} bytes is none of them.");
    }
  }
}
