using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.X3f;

/// <summary>The three stacked layers of a Foveon sensor, as the camera recorded them.</summary>
/// <param name="Width">Columns of the sensor data.</param>
/// <param name="Height">Rows of the sensor data.</param>
/// <param name="Layers">Three 16-bit samples a site — top, middle and bottom layer — rows from the top, unprocessed.</param>
public sealed record X3fSensor(int Width, int Height, ushort[] Layers);

/// <summary>Undoes the Foveon storages of an X3F raw section: the Huffman codings and the TRUE engines.</summary>
/// <remarks>
/// Converted from Roland Karlsson's BSD-licensed x3f tools as LibRaw 0.20.2 carries them
/// (<c>src/x3f/x3f_utils_patched.cpp</c> and <c>x3f_parse_process.cpp</c>); see
/// <c>THIRD-PARTY-NOTICE.LibRaw.txt</c>. The layers come out exactly as LibRaw's unpacked
/// <c>color3_image</c>: no black level, gain or colour correction is applied, since those need the
/// camera's correction block and a processing chain the format does not define.
/// </remarks>
internal static class X3fSensorDecoder {

  internal const uint RawHuffmanX530 = 0x00030005;
  internal const uint RawHuffman10Bit = 0x00030006;
  internal const uint RawTrue = 0x0003001E;
  internal const uint RawMerrill = 0x0001001E;
  internal const uint RawQuattro = 0x00010023;
  internal const uint RawSdq = 0x00010025;
  internal const uint RawSdqh = 0x00010027;
  internal const uint RawSdqh2 = 0x00010029;

  /// <summary>Whether a section's type and format are one of the raw sensor storages.</summary>
  internal static bool IsRaw(uint typeFormat) => typeFormat is RawHuffmanX530 or RawHuffman10Bit or RawTrue or RawMerrill or RawQuattro or RawSdq or RawSdqh or RawSdqh2;

  /// <summary>The order LibRaw picks the raw section in when a file has more than one.</summary>
  internal static int Preference(uint typeFormat) => typeFormat switch {
    RawHuffmanX530 => 0,
    RawHuffman10Bit => 1,
    RawTrue => 2,
    RawMerrill => 3,
    RawQuattro => 4,
    RawSdq => 5,
    RawSdqh => 6,
    _ => 7,
  };

  private static bool _IsQuattro(uint typeFormat) => typeFormat is RawQuattro or RawSdq or RawSdqh or RawSdqh2;

  /// <summary>Decodes one raw section; <paramref name="body"/> starts after its 28-byte header.</summary>
  /// <param name="swapRedBlue">The Polaroid x530 stores its layers the other way round, and LibRaw swaps them back.</param>
  internal static X3fSensor Decode(ReadOnlySpan<byte> body, uint typeFormat, int columns, int rows, int rowStride, bool swapRedBlue) {
    if (columns is < 1 or > 65535 || rows is < 1 or > 65535)
      throw new InvalidDataException($"An X3F raw section states {columns}x{rows}.");
    var layers = typeFormat is RawHuffmanX530 or RawHuffman10Bit
      ? _Huffman(body, columns, rows, rowStride)
      : _True(body, typeFormat, columns, rows);
    if (swapRedBlue)
      for (var p = 0; p < layers.Length; p += 3)
        (layers[p], layers[p + 2]) = (layers[p + 2], layers[p]);
    return new(columns, rows, layers);
  }

  // ============================================================================================
  // Huffman trees
  // ============================================================================================

  private sealed class Tree {
    private readonly List<int[]> _branches = [];
    private readonly List<uint> _leaves = [];
    private const uint _UNDEFINED = 0xFFFFFFFF;

    internal Tree() => this._NewNode();

    private int _NewNode() {
      this._branches.Add([-1, -1]);
      this._leaves.Add(_UNDEFINED);
      return this._branches.Count - 1;
    }

    /// <summary>Adds a code of <paramref name="length"/> bits, most significant first.</summary>
    internal void Add(int length, uint code, uint value) {
      var node = 0;
      for (var i = 0; i < length; ++i) {
        var bit = (int)((code >> (length - i - 1)) & 1);
        var next = this._branches[node][bit];
        if (next < 0) {
          next = this._NewNode();
          this._branches[node][bit] = next;
        }

        node = next;
      }

      this._leaves[node] = value;
    }

    /// <summary>Walks the tree; <c>null</c> where the bits lead off it.</summary>
    internal uint? Read(ref BitSource bits) {
      var node = 0;
      while (this._branches[node][0] >= 0 || this._branches[node][1] >= 0) {
        node = this._branches[node][bits.Next()];
        if (node < 0)
          return null;
      }

      return this._leaves[node];
    }
  }

  /// <summary>Bits most significant first; a read past the end yields noughts.</summary>
  private ref struct BitSource(ReadOnlySpan<byte> data, int start) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at = start;
    private int _bit = 8;
    private int _byte;

    internal int Next() {
      if (this._bit == 8) {
        this._byte = this._at < this._data.Length ? this._data[this._at] : 0;
        ++this._at;
        this._bit = 0;
      }

      return (this._byte >> (7 - this._bit++)) & 1;
    }
  }

  // ============================================================================================
  // The Huffman storages: 10-bit samples coded as differences, compressed or packed
  // ============================================================================================

  private static ushort[] _Huffman(ReadOnlySpan<byte> body, int columns, int rows, int rowStride) {
    const int bits = 10;
    const int tableSize = 1 << bits;
    if (body.Length < 2 * tableSize)
      throw new InvalidDataException("An X3F Huffman raw section ends inside its mapping table.");
    var mapping = new ushort[tableSize];
    for (var i = 0; i < tableSize; ++i)
      mapping[i] = BinaryPrimitives.ReadUInt16LittleEndian(body[(2 * i)..]);
    var at = 2 * tableSize;
    var layers = new ushort[columns * rows * 3];

    if (rowStride != 0) {
      // Packed: each site one little-endian word of three 10-bit indices into the mapping.
      var data = body[at..];
      for (var row = 0; row < rows; ++row) {
        if ((long)row * rowStride + (long)columns * 4 > data.Length)
          throw new InvalidDataException("An X3F packed raw section ends before its last row.");
        ushort c0 = 0, c1 = 0, c2 = 0;
        for (var col = 0; col < columns; ++col) {
          var word = BinaryPrimitives.ReadUInt32LittleEndian(data[(row * rowStride + 4 * col)..]);
          for (var color = 0; color < 3; ++color) {
            var index = (int)((word >> (color * bits)) & (tableSize - 1));
            ref var c = ref color == 0 ? ref c0 : ref color == 1 ? ref c1 : ref c2;
            c = (ushort)(c + mapping[index]);
            layers[3 * (row * columns + col) + color] = (short)c > 0 ? c : (ushort)0;
          }
        }
      }

      return layers;
    }

    // Compressed: a table of 1,024 codes (length in the top five bits), the bit stream, and a row
    // offset table at the end of the section.
    if (body.Length < at + 4 * tableSize + 4 * rows)
      throw new InvalidDataException("An X3F Huffman raw section ends inside its code table.");
    var tree = new Tree();
    for (var i = 0; i < tableSize; ++i) {
      var element = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 4 * i)..]);
      if (element != 0)
        tree.Add((int)((element >> 27) & 0x1F), element & 0x07FFFFFF, mapping[i]);
    }

    at += 4 * tableSize;
    var stream = body[at..(body.Length - 4 * rows)];
    var rowOffsets = new uint[rows];
    for (var row = 0; row < rows; ++row)
      rowOffsets[row] = BinaryPrimitives.ReadUInt32LittleEndian(body[(body.Length - 4 * rows + 4 * row)..]);

    // LibRaw decodes once from nought; where some sum went negative it decodes again with the
    // whole picture raised by the lowest such sum.
    var minimum = 0;
    _HuffmanRows(stream, tree, rowOffsets, columns, rows, 0, layers, ref minimum);
    if (minimum < 0)
      _HuffmanRows(stream, tree, rowOffsets, columns, rows, -minimum, layers, ref minimum);
    return layers;
  }

  private static void _HuffmanRows(ReadOnlySpan<byte> stream, Tree tree, uint[] rowOffsets, int columns, int rows, int offset, ushort[] layers, ref int minimum) {
    for (var row = 0; row < rows; ++row) {
      if (rowOffsets[row] > stream.Length - 1)
        throw new InvalidDataException($"Row {row} of an X3F Huffman raw section starts past its data.");
      var bits = new BitSource(stream, (int)rowOffsets[row]);
      short c0 = (short)offset, c1 = (short)offset, c2 = (short)offset;
      for (var col = 0; col < columns; ++col)
        for (var color = 0; color < 3; ++color) {
          var diff = tree.Read(ref bits) ?? throw new InvalidDataException($"Row {row} of an X3F Huffman raw section holds a code its table does not.");
          ref var c = ref color == 0 ? ref c0 : ref color == 1 ? ref c1 : ref c2;
          c = (short)(c + (int)diff);
          ushort value;
          if (c < 0) {
            value = 0;
            if (c < minimum)
              minimum = c;
          } else {
            value = (ushort)c;
          }

          layers[3 * (row * columns + col) + color] = value;
        }
    }
  }

  // ============================================================================================
  // The TRUE engines: three planes of differences, each continuing the same-parity neighbour
  // ============================================================================================

  private static ushort[] _True(ReadOnlySpan<byte> body, uint typeFormat, int columns, int rows) {
    var at = 0;
    var planeColumns = new int[3];
    var planeRows = new int[3];
    var quattroLayout = false;
    if (_IsQuattro(typeFormat)) {
      for (var i = 0; i < 3; ++i) {
        planeColumns[i] = _U16(body, ref at);
        planeRows[i] = _U16(body, ref at);
      }

      if (planeRows[0] == rows / 2)
        quattroLayout = true;
      else if (planeRows[0] != rows)
        throw new InvalidDataException("An X3F Quattro raw section's planes are neither the full nor the half size.");
    }

    var seeds = new int[3];
    for (var i = 0; i < 3; ++i)
      seeds[i] = _U16(body, ref at);
    _U16(body, ref at); // unknown

    var tree = new Tree();
    for (var index = 0u; ; ++index) {
      if (at + 2 > body.Length)
        throw new InvalidDataException("An X3F TRUE raw section ends inside its code table.");
      int length = body[at], code = body[at + 1];
      at += 2;
      if (length == 0)
        break;
      tree.Add(length, (uint)(code >> (8 - length)) & 0xFF, index);
    }

    if (_IsQuattro(typeFormat))
      at += 4; // unknown

    var planeSizes = new uint[3];
    for (var i = 0; i < 3; ++i) {
      if (at + 4 > body.Length)
        throw new InvalidDataException("An X3F TRUE raw section ends inside its plane sizes.");
      planeSizes[i] = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
      at += 4;
    }

    var data = body[at..];
    var planeStart = new int[3];
    for (var i = 1; i < 3; ++i)
      planeStart[i] = checked(planeStart[i - 1] + (int)((planeSizes[i - 1] + 15) / 16 * 16));

    var layers = new ushort[columns * rows * 3];
    ushort[]? top = null;
    int topColumns = 0, topRows = 0;
    int areaColumns = columns, areaRows = rows;
    if (_IsQuattro(typeFormat) && quattroLayout) {
      areaColumns = planeColumns[0];
      areaRows = planeRows[0];
      topColumns = planeColumns[2];
      topRows = planeRows[2];
      top = new ushort[topColumns * topRows];
    }

    var area = new ushort[areaColumns * areaRows * 3];
    for (var color = 0; color < 3; ++color) {
      int planeRowsHere = rows, planeColumnsHere = columns;
      ushort[] target = area;
      int targetColumns = areaColumns, targetRows = areaRows, channels = 3, offset = color;
      if (_IsQuattro(typeFormat)) {
        planeRowsHere = planeRows[color];
        planeColumnsHere = planeColumns[color];
        if (quattroLayout && color == 2) {
          target = top!;
          targetColumns = topColumns;
          targetRows = topRows;
          channels = 1;
          offset = 0;
        }
      }

      if (planeRowsHere != targetRows || planeColumnsHere < targetColumns)
        throw new InvalidDataException("An X3F TRUE plane does not match the picture it fills.");
      if (planeStart[color] > data.Length)
        throw new InvalidDataException("An X3F TRUE plane starts past the section's end.");
      _TruePlane(data, planeStart[color], tree, seeds[color], planeColumnsHere, planeRowsHere, target, targetColumns, channels, offset);
    }

    if (top == null)
      return area;

    // The Quattro layout: the two lower layers at half size, placed on every other site, and the
    // top layer at full size — as LibRaw places them.
    for (var row = 0; row < areaRows && row < rows / 2; ++row)
      for (var col = 0; col < areaColumns && col < columns / 2; ++col) {
        var destination = 3 * ((2 * row) * columns + 2 * col);
        layers[destination] = area[3 * (row * areaColumns + col)];
        layers[destination + 1] = area[3 * (row * areaColumns + col) + 1];
      }

    for (var row = 0; row < topRows && row < rows; ++row)
      for (var col = 0; col < topColumns && col < columns; ++col)
        layers[3 * (row * columns + col) + 2] = top[row * topColumns + col];
    return layers;
  }

  private static void _TruePlane(ReadOnlySpan<byte> data, int start, Tree tree, int seed, int columns, int rows, ushort[] target, int targetColumns, int channels, int offset) {
    var bits = new BitSource(data, start);
    Span<int> rowStart = [seed, seed, seed, seed];
    Span<int> accumulator = [0, 0];
    var destination = offset;
    for (var row = 0; row < rows; ++row) {
      var oddRow = row & 1;
      for (var col = 0; col < columns; ++col) {
        var oddCol = col & 1;
        var diff = _TrueDiff(ref bits, tree);
        var previous = col < 2 ? rowStart[2 * oddRow + oddCol] : accumulator[oddCol];
        var value = previous + diff;
        accumulator[oddCol] = value;
        if (col < 2)
          rowStart[2 * oddRow + oddCol] = value;
        if (col >= targetColumns)
          continue; // the binned Quattro plane runs wider than it is shown
        target[destination] = (ushort)value;
        destination += channels;
      }
    }
  }

  /// <summary>A code naming how many bits follow, then that many bits of a signed difference.</summary>
  private static int _TrueDiff(ref BitSource bits, Tree tree) {
    if (tree.Read(ref bits) is not { } length)
      return 0;
    if (length == 0)
      return 0;
    var first = bits.Next();
    var diff = first;
    for (var i = 1; i < length; ++i)
      diff = (diff << 1) + bits.Next();
    if (first == 0)
      diff -= (1 << (int)length) - 1;
    return diff;
  }

  private static int _U16(ReadOnlySpan<byte> body, ref int at) {
    if (at + 2 > body.Length)
      throw new InvalidDataException("An X3F TRUE raw section ends inside its header.");
    var value = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
    at += 2;
    return value;
  }
}
