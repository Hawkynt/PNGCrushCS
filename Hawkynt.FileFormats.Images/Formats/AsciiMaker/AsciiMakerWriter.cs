using System;
using System.IO;

namespace FileFormat.AsciiMaker;

/// <summary>Assembles an ASCII maker screen, which is the grid of character codes and nothing else.</summary>
public static class AsciiMakerWriter {

  public static byte[] ToBytes(AsciiMakerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var characters = file.Characters ?? new byte[AsciiMakerFile.ScreenSize];
    var result = new byte[AsciiMakerFile.ScreenSize];
    characters.AsSpan(0, Math.Min(characters.Length, AsciiMakerFile.ScreenSize)).CopyTo(result);

    return result;
  }

  public static void ToFile(AsciiMakerFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
