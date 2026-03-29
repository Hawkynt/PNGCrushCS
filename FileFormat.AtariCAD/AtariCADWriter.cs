using System;

namespace FileFormat.AtariCAD;

/// <summary>Assembles Atari CAD Screen bytes from an <see cref="AtariCADFile"/>.</summary>
public static class AtariCADWriter {

  public static byte[] ToBytes(AtariCADFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariCADFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariCADFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
