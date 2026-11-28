using System;

namespace FileFormat.Eri;

/// <summary>The four orders the samples of a lossless block may be sent in.</summary>
/// <remarks>
/// A block is held channel by channel, each channel row by row. The extended format may send it
/// column by column instead, or with the channels interleaved sample by sample, along rows or
/// along columns. Each table maps the n-th sample sent to its place in the block.
/// </remarks>
internal static class EriArrangement {

  public static int[][] Tables(int size, int channels) {
    var area = size * size;
    var samples = area * channels;
    var tables = new int[4][];

    tables[0] = new int[samples];
    for (var i = 0; i < samples; ++i)
      tables[0][i] = i;

    // Column by column, channel after channel.
    tables[1] = new int[samples];
    var n = 0;
    for (var c = 0; c < channels; ++c)
      for (var x = 0; x < size; ++x)
        for (var y = 0; y < size; ++y)
          tables[1][n++] = c * area + y * size + x;

    // Along rows, the channels of each pixel together.
    tables[2] = new int[samples];
    n = 0;
    for (var i = 0; i < area; ++i)
      for (var c = 0; c < channels; ++c)
        tables[2][n++] = c * area + i;

    // Along columns, the channels of each pixel together.
    tables[3] = new int[samples];
    n = 0;
    for (var x = 0; x < size; ++x)
      for (var y = 0; y < size; ++y)
        for (var c = 0; c < channels; ++c)
          tables[3][n++] = c * area + y * size + x;

    return tables;
  }
}

/// <summary>Undoes and applies the per-block transform of a lossless ERI picture.</summary>
/// <remarks>
/// The operation code is DFC(2) ARC(2) COPN(4): which differences were taken (bit 0 across, bit 1
/// down — the standard format always takes both and writes its 4-bit code with 11 in front), the
/// arrangement, and the colour operation. The colour operations subtract one of blue, green and
/// red from one or both of the others, channels being numbered blue, green, red, alpha:
/// <code>
/// 0101 G-B   0110 R-B   0111 G-B R-B
/// 1001 B-G   1010 R-G   1011 B-G R-G
/// 1101 B-R   1110 G-R   1111 B-R G-R
/// </code>
/// and every other code leaves the channels alone. An operation code of zero means the block is
/// not differenced at all, which is how a paletted picture's indices are sent.
/// </remarks>
internal static class EriBlockOperation {

  /// <summary>For each colour operation, (base channel, first target, second target or -1).</summary>
  internal static readonly (int Base, int First, int Second)[] ColorOperations = [
    (-1, -1, -1), (-1, -1, -1), (-1, -1, -1), (-1, -1, -1),
    (-1, -1, -1), (0, 1, -1), (0, 2, -1), (0, 1, 2),
    (-1, -1, -1), (1, 0, -1), (1, 2, -1), (1, 0, 2),
    (-1, -1, -1), (2, 0, -1), (2, 1, -1), (2, 0, 1),
  ];

  /// <summary>Rebuilds a block of samples from what was sent for it.</summary>
  /// <param name="operation">The block's operation code.</param>
  /// <param name="sent">The samples in the order they were sent.</param>
  /// <param name="block">Receives the block's samples.</param>
  /// <param name="arrangements">The four arrangement tables.</param>
  /// <param name="column">The rightmost sample of each channel row of the block to the left.</param>
  /// <param name="line">The bottom row of each channel of the block above.</param>
  /// <param name="size">The block's side.</param>
  /// <param name="channels">The number of channels.</param>
  public static void Undo(int operation, ReadOnlySpan<sbyte> sent, Span<sbyte> block, int[][] arrangements,
    Span<sbyte> column, Span<sbyte> line, int size, int channels) {
    var area = size * size;
    var arrangement = (operation >> 4) & 3;
    if (arrangement == 0) {
      sent.CopyTo(block);
      if (operation == 0)
        return;
    } else {
      var table = arrangements[arrangement];
      for (var i = 0; i < sent.Length; ++i)
        block[table[i]] = sent[i];
    }

    var (source, first, second) = ColorOperations[operation & 0x0F];
    if (source >= 0)
      for (var i = 0; i < area; ++i) {
        var value = block[source * area + i];
        block[first * area + i] += value;
        if (second >= 0)
          block[second * area + i] += value;
      }

    var rowsOfBlock = size * channels;
    if ((operation & 0x40) != 0)
      for (var r = 0; r < rowsOfBlock; ++r) {
        var running = column[r];
        var at = r * size;
        for (var x = 0; x < size; ++x)
          block[at + x] = running += block[at + x];
        column[r] = running;
      }
    else
      for (var r = 0; r < rowsOfBlock; ++r)
        column[r] = block[r * size + size - 1];

    // Down every column, starting from the row above the block, whatever bit 1 says: the formats
    // that exist always take this difference.
    for (var c = 0; c < channels; ++c) {
      var above = line.Slice(c * size, size);
      for (var y = 0; y < size; ++y) {
        var at = c * area + y * size;
        for (var x = 0; x < size; ++x) {
          block[at + x] += y == 0 ? above[x] : block[at - size + x];
        }
      }

      block.Slice(c * area + (size - 1) * size, size).CopyTo(above);
    }
  }
}
