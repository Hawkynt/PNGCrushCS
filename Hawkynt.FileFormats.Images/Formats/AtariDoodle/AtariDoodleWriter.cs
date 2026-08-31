using System;
using System.IO;

namespace FileFormat.AtariDoodle;

/// <summary>Writes original Atari ST Doodle (.DOO) monochrome screen dumps.</summary>
public static class AtariDoodleWriter {

  /// <summary>Returns the exact 32,000 bytes of Atari ST screen memory.</summary>
  public static byte[] ToBytes(AtariDoodleFile file) {
    AtariDoodleFile.Validate(file, nameof(file));
    return file.ScreenData[..];
  }

  /// <summary>Writes a Doodle screen to a stream.</summary>
  public static void ToStream(AtariDoodleFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  /// <summary>Writes a Doodle screen to disk.</summary>
  public static void ToFile(AtariDoodleFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
