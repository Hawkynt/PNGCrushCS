using System;
using System.IO;

namespace FileFormat.AtariIce;

/// <summary>Writes an Interlace Character Editor picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole rather than unpacking it, because thirty-three pairings each
/// place their colour bytes differently and only the pairing byte says which — so there is nothing
/// to reassemble here, only the length to insist on. A file one byte short of what its pairing
/// declares is not a shorter picture but a different one, and the reader would read it as such.
/// </remarks>
public static class AtariIceWriter {

  public static byte[] ToBytes(AtariIceFile file) {
    var data = file.Data;
    if (data == null || data.Length <= 1024)
      throw new InvalidDataException("Nothing to write: a picture is at least a character set long.");

    return data.AsSpan().ToArray();
  }
}
