using System;

namespace FileFormat.DaliST;

/// <summary>Assembles Atari ST Dali (SD0/SD1/SD2) image bytes from a DaliSTFile.</summary>
public static class DaliSTWriter {

  public static byte[] ToBytes(DaliSTFile file) {
    DaliSTFile.Validate(file, nameof(file));

    var result = new byte[DaliSTFile.ExpectedFileSize];
    new DaliSTHeader(file.Palette).WriteTo(result.AsSpan(DaliSTFile.PaletteOffset));
    file.ReservedData?.CopyTo(result, DaliSTFile.ReservedOffset);
    file.PixelData.CopyTo(result, DaliSTFile.HeaderSize);
    return result;
  }
}
