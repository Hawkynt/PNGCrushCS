using System;

namespace FileFormat.AtariTools800;

/// <summary>Assembles AtariTools-800 sprite dump bytes from an <see cref="AtariTools800File"/>.</summary>
public static class AtariTools800Writer {

  public static byte[] ToBytes(AtariTools800File file) {
    var kind = file.Kind;
    var result = new byte[AtariTools800File.FileSizeFor(kind)];

    _Copy(file.Colors, result, 0, AtariTools800File.SpriteCount);
    if (AtariTools800File.HasPlayers(kind))
      _Copy(file.PlayerData, result, AtariTools800File.SpriteCount, AtariTools800File.SpriteCount * AtariTools800File.PlayerDataSize);

    if (AtariTools800File.HasMissiles(kind))
      _Copy(file.MissileData, result, AtariTools800File.MissileDataOffsetFor(kind), AtariTools800File.MissileDataSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
