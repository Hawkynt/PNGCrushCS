using System;
using System.IO;

namespace FileFormat.TrzmielCompressed;

/// <summary>Assembles a Trzmiel picture, stored rather than packed.</summary>
/// <remarks>
/// The format offers three arrangements and the first of them is no packing at all, which every
/// reader of it accepts. A picture written that way is larger and exactly as correct; the two packed
/// arrangements save space and would need a run-length encoder to match the one that unpacks them.
/// </remarks>
public static class TrzmielCompressedWriter {

  /// <summary>The arrangement byte that says the rows follow as they are.</summary>
  public const byte Stored = 0;

  public static byte[] ToBytes(TrzmielCompressedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var screen = file.ScreenData ?? new byte[TrzmielCompressedFile.ScreenSize];
    var result = new byte[1 + TrzmielCompressedFile.ScreenSize];
    result[0] = Stored;
    screen.AsSpan(0, Math.Min(screen.Length, TrzmielCompressedFile.ScreenSize)).CopyTo(result.AsSpan(1));

    return result;
  }

  public static void ToFile(TrzmielCompressedFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
