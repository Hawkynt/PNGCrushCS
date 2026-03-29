using System;

namespace FileFormat.AtariPlayer;

/// <summary>Assembles Atari 8-bit Player/Missile Graphics bytes from an <see cref="AtariPlayerFile"/>.</summary>
public static class AtariPlayerWriter {

  public static byte[] ToBytes(AtariPlayerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariPlayerFile.FileSize];
    file.PlayerData.AsSpan(0, Math.Min(file.PlayerData.Length, AtariPlayerFile.FileSize)).CopyTo(result);
    return result;
  }
}
