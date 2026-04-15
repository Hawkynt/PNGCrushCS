using System;

namespace FileFormat.DaliST;

/// <summary>Assembles Atari ST Dali (SD0/SD1/SD2) image bytes from a DaliSTFile.</summary>
public static class DaliSTWriter {

  public static byte[] ToBytes(DaliSTFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DaliSTFile.ExpectedFileSize];

    new DaliSTHeader(file.Palette).WriteTo(result);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DaliSTFile.PlanarDataSize)).CopyTo(result.AsSpan(DaliSTFile.PaletteSize));

    return result;
  }
}
