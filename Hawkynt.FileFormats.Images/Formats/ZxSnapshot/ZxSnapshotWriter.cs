using System;

namespace FileFormat.ZxSnapshot;

/// <summary>Assembles a 48K snapshot: the registers, then memory with the screen at its start.</summary>
/// <remarks>
/// Nothing in a snapshot says what it is — no magic, no header field — and its length is the only
/// thing telling one from any other block of memory. So this writes the shortest of the three
/// lengths the reader knows, exactly, and any other would be refused on the way back in.
/// </remarks>
public static class ZxSnapshotWriter {

  public static byte[] ToBytes(ZxSnapshotFile file) {
    var screen = file.Screen ?? [];
    var result = new byte[ZxSnapshotFile.ShortFileSize];

    // Only the low three bits reach the border; the rest of that byte is other machine state, and
    // this writes a machine that is not running anything.
    result[ZxSnapshotFile.BorderOffset] = (byte)(file.BorderColor & 7);

    screen.AsSpan(0, Math.Min(screen.Length, ZxSnapshotFile.ScreenSize))
      .CopyTo(result.AsSpan(ZxSnapshotFile.HeaderSize));

    return result;
  }
}
