using System;

namespace FileFormat.EpaBios;

/// <summary>Assembles Award BIOS Logo (.epa) file bytes.</summary>
public static class EpaBiosWriter {

  public static byte[] ToBytes(EpaBiosFile file) {
    var result = new byte[EpaBiosFile.SizeOf(file.Columns, file.Rows)];
    result[0] = (byte)file.Columns;
    result[1] = (byte)file.Rows;

    file.Attributes.CopyTo(result.AsSpan(2));
    file.Glyphs.CopyTo(result.AsSpan(2 + file.Attributes.Length));

    // The trailer is left as it is found: nothing reads it, and a BIOS that writes one puts its own
    // second logo there rather than anything derivable from the picture.
    return result;
  }
}
