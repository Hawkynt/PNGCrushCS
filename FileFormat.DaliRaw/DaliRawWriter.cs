using System;

namespace FileFormat.DaliRaw;

/// <summary>Assembles Atari ST Dali raw image bytes from a DaliRawFile.</summary>
public static class DaliRawWriter {

  public static byte[] ToBytes(DaliRawFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DaliRawFile.ExpectedFileSize];

    new DaliRawHeader(file.Palette).WriteTo(result);

    // 2 bytes padding at offset 32 are left as zero
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DaliRawFile.PlanarDataSize)).CopyTo(result.AsSpan(DaliRawFile.PaletteSize + DaliRawFile.PaddingSize));

    return result;
  }
}
