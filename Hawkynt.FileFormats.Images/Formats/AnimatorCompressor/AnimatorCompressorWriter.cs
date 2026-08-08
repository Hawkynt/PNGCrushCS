using System;
using System.Collections.Generic;

namespace FileFormat.AnimatorCompressor;

/// <summary>Assembles a Kompresor do Animatora sheet from an <see cref="AnimatorCompressorFile"/>.</summary>
public static class AnimatorCompressorWriter {

  /// <summary>Where the loader put the animation, which is the only thing the header states.</summary>
  private const int _LOAD_ADDRESS = 0x2000;

  /// <summary>Writes the sheet, which is already whole because its map and tiles are one block.</summary>
  public static byte[] ToBytes(AnimatorCompressorFile file) => (byte[])(file.Data ?? []).Clone();

  /// <summary>
  /// Builds the file around a map and a tile set, giving it the executable header that is the whole
  /// of its signature.
  /// </summary>
  public static byte[] Assemble(int frames, int columns, int rows, ReadOnlySpan<byte> map, List<byte[]> tiles) {
    ArgumentNullException.ThrowIfNull(tiles);

    var length = AnimatorCompressorFile.MapOffset + map.Length + tiles.Count * AnimatorCompressorFile.TileLength;
    var data = new byte[length];

    // An Atari executable: two 255 bytes, then the first and last address the block occupies.
    var end = _LOAD_ADDRESS + length - 6 - 1;
    data[0] = 255;
    data[1] = 255;
    data[2] = _LOAD_ADDRESS & 255;
    data[3] = _LOAD_ADDRESS >> 8;
    data[4] = (byte)end;
    data[5] = (byte)(end >> 8);
    data[8] = (byte)frames;
    data[9] = (byte)columns;
    data[10] = (byte)rows;

    map.CopyTo(data.AsSpan(AnimatorCompressorFile.MapOffset));

    var at = AnimatorCompressorFile.MapOffset + map.Length;
    foreach (var tile in tiles) {
      tile.CopyTo(data, at);
      at += AnimatorCompressorFile.TileLength;
    }

    return data;
  }
}
