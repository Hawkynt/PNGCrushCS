using System;
using System.IO;

namespace FileFormat.Bfli;

/// <summary>Assembles BFLI (.bfl/.bfli/.flp) file bytes from a BfliFile.</summary>
public static class BfliWriter {

  /// <summary>Writes the three header bytes and the payload behind them.</summary>
  /// <remarks>
  /// The payload is already in the order the file stores it — the interleaving is done where the
  /// picture is built, so that reading a file and writing it back gives the same bytes.
  /// </remarks>
  public static byte[] ToBytes(BfliFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.RawData is not { Length: BfliFile.PayloadSize })
      throw new InvalidDataException($"A BFLI picture holds {BfliFile.PayloadSize} bytes of payload.");

    var result = new byte[BfliFile.FileSize];
    result[0] = (byte)(BfliFile.LoadAddress & 0xFF);
    result[1] = (byte)(BfliFile.LoadAddress >> 8);
    result[2] = BfliFile.Marker;
    file.RawData.CopyTo(result.AsSpan(BfliFile.HeaderSize));

    return result;
  }
}
