using System;

namespace FileFormat.SemiGraphicLogo;

/// <summary>Assembles Semi-Graphic logos screen bytes from a <see cref="SemiGraphicLogoFile"/>.</summary>
public static class SemiGraphicLogoWriter {

  /// <summary>Writes the character codes, which are the whole of the file.</summary>
  public static byte[] ToBytes(SemiGraphicLogoFile file) {
    var data = new byte[SemiGraphicLogoFile.FileSize];
    var characters = file.Characters ?? [];
    characters.AsSpan(0, Math.Min(characters.Length, data.Length)).CopyTo(data);

    return data;
  }
}
