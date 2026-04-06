using System;
using System.IO;

namespace FileFormat.AtariPlayer;

/// <summary>Reads Atari 8-bit Player/Missile Graphics from bytes, streams, or file paths.</summary>
public static class AtariPlayerReader {

  public static AtariPlayerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Player/Missile file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPlayerFile FromStream(Stream stream) {
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

  public static AtariPlayerFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AtariPlayerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariPlayerFile.FileSize)
      throw new InvalidDataException($"Invalid Atari Player/Missile data size: expected exactly {AtariPlayerFile.FileSize} bytes, got {data.Length}.");

    var playerData = new byte[AtariPlayerFile.FileSize];
    data.AsSpan(0, AtariPlayerFile.FileSize).CopyTo(playerData);

    return new AtariPlayerFile { PlayerData = playerData };
  }
}
