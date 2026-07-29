using System;

namespace FileFormat.AtariTxs;

/// <summary>Assembles Atari 8-bit .txs texture bytes.</summary>
public static class AtariTxsWriter {

  public static byte[] ToBytes(AtariTxsFile file) {
    var result = new byte[AtariTxsFile.FileSize];
    AtariTxsFile.Header.CopyTo(result);

    var values = file.Values ?? [];
    for (var i = 0; i < values.Length && i < AtariTxsFile.FileSize - AtariTxsFile.Header.Length; ++i)
      result[AtariTxsFile.Header.Length + i] = (byte)(values[i] & 15);

    return result;
  }
}
