using System;

namespace FileFormat.SevenuP;

/// <summary>Assembles ZX Spectrum SevenuP (.sev) file bytes.</summary>
public static class SevenuPWriter {

  public static byte[] ToBytes(SevenuPFile file) {
    var result = new byte[SevenuPFile.FileSizeFor(file.Width, file.Height)];

    SevenuPFile.Signature.CopyTo(result);
    // Bytes 3, 7 and 8 stay zero; byte 6 is the version marker readers check for.
    result[6] = 1;
    result[SevenuPFile.WidthOffset] = (byte)file.Width;
    result[SevenuPFile.WidthOffset + 1] = (byte)(file.Width >> 8);
    result[SevenuPFile.HeightOffset] = (byte)file.Height;
    result[SevenuPFile.HeightOffset + 1] = (byte)(file.Height >> 8);

    var cells = file.CellData ?? [];
    var length = Math.Min(cells.Length, result.Length - SevenuPFile.CellDataOffset);
    cells.AsSpan(0, length).CopyTo(result.AsSpan(SevenuPFile.CellDataOffset));

    return result;
  }
}
