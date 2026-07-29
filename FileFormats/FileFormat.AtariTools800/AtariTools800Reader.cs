using System;
using System.IO;

namespace FileFormat.AtariTools800;

/// <summary>Reads AtariTools-800 sprite dumps from bytes, streams, or file paths.</summary>
public static class AtariTools800Reader {

  public static AtariTools800File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AtariTools-800 sprite dump not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariTools800File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AtariTools800File FromSpan(ReadOnlySpan<byte> data) {
    // The three kinds have three distinct sizes, so the bytes say which one this is.
    AtariTools800Kind? found = null;
    foreach (var candidate in Enum.GetValues<AtariTools800Kind>())
      if (AtariTools800File.FileSizeFor(candidate) == data.Length)
        found = candidate;

    if (found is not { } kind)
      throw new InvalidDataException($"{data.Length} bytes is not the size of any AtariTools-800 sprite dump.");

    var colors = new byte[AtariTools800File.SpriteCount];
    data[..AtariTools800File.SpriteCount].CopyTo(colors);

    var players = Array.Empty<byte>();
    if (AtariTools800File.HasPlayers(kind)) {
      players = new byte[AtariTools800File.SpriteCount * AtariTools800File.PlayerDataSize];
      data.Slice(AtariTools800File.SpriteCount, players.Length).CopyTo(players);
    }

    var missiles = Array.Empty<byte>();
    if (AtariTools800File.HasMissiles(kind)) {
      missiles = new byte[AtariTools800File.MissileDataSize];
      data.Slice(AtariTools800File.MissileDataOffsetFor(kind), missiles.Length).CopyTo(missiles);
    }

    return new() { Kind = kind, Colors = colors, PlayerData = players, MissileData = missiles };
  }

  public static AtariTools800File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
