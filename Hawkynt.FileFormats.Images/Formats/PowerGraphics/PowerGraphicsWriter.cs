using System;
using System.IO;

namespace FileFormat.PowerGraphics;

/// <summary>Writes a PowerGraphics picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole — a display list, a raster program and a playfield addressed by
/// where the machine loaded them, not by any offset that could be recomputed — so there is nothing
/// to reassemble here, only the executable header's own account of the length to insist on.
/// </remarks>
public static class PowerGraphicsWriter {

  public static byte[] ToBytes(PowerGraphicsFile file) {
    var data = file.Data;
    if (data == null || data.Length < 1776)
      throw new InvalidDataException("Nothing to write: a PowerGraphics picture is at least 1776 bytes.");

    return data.AsSpan().ToArray();
  }
}
