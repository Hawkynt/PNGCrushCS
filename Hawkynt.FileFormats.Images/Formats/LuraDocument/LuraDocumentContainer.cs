using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.LuraDocument;

/// <summary>One image file directory of a LuraDocument file, reduced to the fields the format uses.</summary>
internal sealed record LuraDocumentDirectory {
  public int Width { get; init; }
  public int Height { get; init; }
  public int Compression { get; init; }
  public int SamplesPerPixel { get; init; } = 1;
  public int BitsPerSample { get; init; } = 1;
  public (uint Numerator, uint Denominator)? XResolution { get; init; }
  public (uint Numerator, uint Denominator)? YResolution { get; init; }

  /// <summary>The layer name of tag 37637: <c>bit</c>, <c>for</c>, <c>bac</c>, <c>thm</c> or <c>txt</c>; empty when absent.</summary>
  public string Name { get; init; } = "";

  /// <summary>Every strip of the directory, joined.</summary>
  public byte[] Data { get; init; } = [];
}

/// <summary>
/// The TIFF wrapping of LuraDocument: little-endian TIFF whose directory chain holds, per page, a page directory
/// (named <c>bit</c>) followed by the directories of that page's layers.
/// </summary>
internal static class LuraDocumentContainer {

  internal const int TagWidth = 256;
  internal const int TagHeight = 257;
  internal const int TagBitsPerSample = 258;
  internal const int TagCompression = 259;
  internal const int TagPhotometric = 262;
  internal const int TagDocumentName = 269;
  internal const int TagImageDescription = 270;
  internal const int TagStripOffsets = 273;
  internal const int TagOrientation = 274;
  internal const int TagSamplesPerPixel = 277;
  internal const int TagStripByteCounts = 279;
  internal const int TagXResolution = 282;
  internal const int TagYResolution = 283;
  internal const int TagPageNumber = 297;
  internal const int TagSoftware = 305;
  internal const int TagIptc = 33723;
  internal const int TagLayerName = 37637;
  internal const int TagAnnotations = 37638;

  /// <summary>TIFF compression value of the scrambled JBIG mask.</summary>
  internal const int CompressionMask = 34690;

  /// <summary>TIFF compression value of a LuraWave layer.</summary>
  internal const int CompressionLuraWave = 34692;

  /// <summary>TIFF compression value LuraTech's encoder reserves for a LuraWave layer of a megabyte or more.</summary>
  internal const int CompressionLuraWaveLarge = 34693;

  /// <summary>Longest directory chain followed; LuraTech's writer allows six pages of at most five directories.</summary>
  private const int _MaxDirectories = 4096;

  /// <summary>Whether the bytes start a little-endian TIFF whose first directory names itself a LuraDocument page or carries the mask compression.</summary>
  /// <returns><see langword="null"/> when the header is too short to tell.</returns>
  public static bool? Recognise(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      return null;
    if (data[0] != 0x49 || data[1] != 0x49 || data[2] != 0x2A || data[3] != 0)
      return false;

    var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    if (offset < 8 || offset + 2 > (ulong)data.Length)
      return null;
    var count = BinaryPrimitives.ReadUInt16LittleEndian(data[(int)offset..]);
    for (var i = 0; i < count; ++i) {
      var entry = (int)offset + 2 + i * 12;
      if (entry + 12 > data.Length)
        return null;
      var tag = BinaryPrimitives.ReadUInt16LittleEndian(data[entry..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(entry + 2)..]);
      if (tag == TagCompression && type == 3)
        return BinaryPrimitives.ReadUInt16LittleEndian(data[(entry + 8)..]) == CompressionMask;

      if (tag == TagLayerName && type == 2)
        return data[(entry + 8)..(entry + 11)].SequenceEqual("bit"u8);
    }

    return false;
  }

  /// <summary>Reads the whole directory chain in file order.</summary>
  /// <exception cref="InvalidDataException">Not little-endian TIFF, or a directory or strip outside the file.</exception>
  public static List<LuraDocumentDirectory> Read(byte[] data) {
    if (data.Length < 8 || data[0] != 0x49 || data[1] != 0x49 || data[2] != 0x2A || data[3] != 0)
      throw new InvalidDataException(data.Length >= 4 && data[0] == 0x4D && data[1] == 0x4D
        ? "Big-endian TIFF is not LuraDocument: LuraTech's files are always little-endian."
        : "Not a LuraDocument file: no little-endian TIFF header.");

    var directories = new List<LuraDocumentDirectory>();
    var seen = new HashSet<uint>();
    var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
    while (offset != 0) {
      if (!seen.Add(offset) || directories.Count >= _MaxDirectories)
        throw new InvalidDataException("The LuraDocument directory chain loops.");
      if (offset + 2L > data.Length)
        throw new InvalidDataException($"A LuraDocument directory at {offset} lies outside the {data.Length}-byte file.");

      var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)offset));
      if (offset + 2L + count * 12L + 4 > data.Length)
        throw new InvalidDataException($"The LuraDocument directory at {offset} runs past the end of the file.");

      var directory = new LuraDocumentDirectory();
      uint[] stripOffsets = [], stripCounts = [];
      for (var i = 0; i < count; ++i) {
        var entry = (int)offset + 2 + i * 12;
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry));
        var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry + 2));
        var values = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 4));
        switch (tag) {
          case TagWidth:
            directory = directory with { Width = (int)Math.Min(_Integers(data, entry, type, values)[0], int.MaxValue) };
            break;
          case TagHeight:
            directory = directory with { Height = (int)Math.Min(_Integers(data, entry, type, values)[0], int.MaxValue) };
            break;
          case TagBitsPerSample:
            directory = directory with { BitsPerSample = (int)_Integers(data, entry, type, values)[0] };
            break;
          case TagCompression:
            directory = directory with { Compression = (int)_Integers(data, entry, type, values)[0] };
            break;
          case TagSamplesPerPixel:
            directory = directory with { SamplesPerPixel = (int)_Integers(data, entry, type, values)[0] };
            break;
          case TagStripOffsets:
            stripOffsets = _Integers(data, entry, type, values);
            break;
          case TagStripByteCounts:
            stripCounts = _Integers(data, entry, type, values);
            break;
          case TagXResolution:
            directory = directory with { XResolution = _Rational(data, entry, type) };
            break;
          case TagYResolution:
            directory = directory with { YResolution = _Rational(data, entry, type) };
            break;
          case TagLayerName when type == 2:
            directory = directory with { Name = _Ascii(data, entry, values) };
            break;
        }
      }

      if (stripOffsets.Length != stripCounts.Length)
        throw new InvalidDataException($"The LuraDocument directory at {offset} lists {stripOffsets.Length} strips but {stripCounts.Length} strip lengths.");

      var joined = new MemoryStream();
      for (var s = 0; s < stripOffsets.Length; ++s) {
        if (stripCounts[s] == 0)
          continue;
        if (stripOffsets[s] + (ulong)stripCounts[s] > (ulong)data.Length)
          throw new InvalidDataException($"A strip of the LuraDocument directory at {offset} lies outside the file.");
        joined.Write(data, (int)stripOffsets[s], (int)stripCounts[s]);
      }

      directories.Add(directory with { Data = joined.ToArray() });
      offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)offset + 2 + count * 12));
    }

    return directories;
  }

  private static int _Size(ushort type) => type switch {
    1 or 2 or 6 or 7 => 1,
    3 or 8 => 2,
    4 or 9 or 11 or 13 => 4,
    5 or 10 or 12 => 8,
    _ => 0,
  };

  private static int _ValueOffset(byte[] data, int entry, ushort type, uint count) {
    var size = (long)_Size(type) * count;
    if (size <= 4)
      return entry + 8;
    var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 8));
    if (offset + size > data.Length)
      throw new InvalidDataException("A LuraDocument tag value lies outside the file.");
    return (int)offset;
  }

  private static uint[] _Integers(byte[] data, int entry, ushort type, uint count) {
    if (type is not (1 or 3 or 4) || count == 0 || count > data.Length)
      throw new InvalidDataException($"A LuraDocument tag that should hold integers has type {type} and {count} values.");
    var at = _ValueOffset(data, entry, type, count);
    var values = new uint[count];
    for (var i = 0; i < count; ++i)
      values[i] = type switch {
        1 => data[at + i],
        3 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + i * 2)),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + i * 4)),
      };
    return values;
  }

  private static (uint, uint)? _Rational(byte[] data, int entry, ushort type) {
    if (type != 5)
      return null;
    var at = _ValueOffset(data, entry, type, 1);
    return (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4)));
  }

  private static string _Ascii(byte[] data, int entry, uint count) {
    if (count == 0)
      return "";
    var at = _ValueOffset(data, entry, 2, count);
    var span = data.AsSpan(at, (int)count);
    var end = span.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
  }

  /// <summary>Builds the directory chain for writing, page by page.</summary>
  internal sealed class Writer {
    private readonly List<List<(ushort Tag, ushort Type, uint Count, byte[] Value)>> _directories = [];
    private readonly List<byte[]> _strips = [];
    private readonly List<bool> _opensPage = [];

    /// <summary>Starts a new directory; later <see cref="Add"/> calls fill it.</summary>
    /// <param name="opensPage">Whether this is a page directory rather than one of the current page's layers.</param>
    public void Begin(bool opensPage) {
      this._directories.Add([]);
      this._strips.Add([]);
      this._opensPage.Add(opensPage);
    }

    public void Add(int tag, ushort type, uint count, byte[] value) => this._directories[^1].Add(((ushort)tag, type, count, value));

    public void Short(int tag, int value) => this.Add(tag, 3, 1, BitConverter.GetBytes((ushort)value));

    public void Long(int tag, int value) => this.Add(tag, 4, 1, BitConverter.GetBytes((uint)value));

    public void Ascii(int tag, string value) {
      var bytes = value.Length == 0 ? [] : Encoding.ASCII.GetBytes(value + "\0");
      this.Add(tag, 2, (uint)bytes.Length, bytes);
    }

    public void Rational(int tag, uint numerator, uint denominator) {
      var bytes = new byte[8];
      BinaryPrimitives.WriteUInt32LittleEndian(bytes, numerator);
      BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), denominator);
      this.Add(tag, 5, 1, bytes);
    }

    /// <summary>Attaches the current directory's single strip; its offset and length tags are filled in on <see cref="ToBytes"/>.</summary>
    public void Strip(byte[] data) => this._strips[^1] = data;

    /// <remarks>
    /// Laid out as LuraTech's writer lays it out, because its reader depends on it. That reader finds a page's layers
    /// by walking on from the page directory for as long as each directory starts right where the previous one ends,
    /// and it does not stop at the next page. So each page's directories are written back to back and followed by
    /// their out-of-line values, which is the gap that ends the page; the strips come last. A directory without data
    /// points its strip at the start of the strip area.
    /// </remarks>
    public byte[] ToBytes() {
      const long headerSize = 8;
      foreach (var entries in this._directories)
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

      // Where every directory and every out-of-line value lands.
      var directoryOffsets = new long[this._directories.Count];
      var valueOffsets = new long[this._directories.Count];
      var position = headerSize;
      for (var first = 0; first < this._directories.Count;) {
        var last = first + 1;
        while (last < this._directories.Count && !this._opensPage[last])
          ++last;
        for (var d = first; d < last; ++d) {
          directoryOffsets[d] = position;
          position += 2 + this._directories[d].Count * 12L + 4;
        }

        for (var d = first; d < last; ++d) {
          valueOffsets[d] = position;
          foreach (var e in this._directories[d])
            if (e.Value.Length > 4)
              position += (e.Value.Length + 1) & ~1;
        }

        first = last;
      }

      var stripArea = position;
      var stripOffsets = new long[this._directories.Count];
      var strip = stripArea;
      for (var d = 0; d < this._directories.Count; ++d) {
        stripOffsets[d] = this._strips[d].Length == 0 ? stripArea : strip;
        strip += this._strips[d].Length;
      }

      var output = new MemoryStream();
      output.Write("II*\0"u8);
      output.Write(BitConverter.GetBytes((uint)headerSize));
      var values = new SortedDictionary<long, byte[]>();
      for (var d = 0; d < this._directories.Count; ++d) {
        _Flush(output, values, directoryOffsets[d]);
        var entries = this._directories[d];
        var valueAt = valueOffsets[d];
        output.Write(BitConverter.GetBytes((ushort)entries.Count));
        foreach (var (tag, type, count, raw) in entries) {
          var value = tag switch {
            TagStripOffsets => BitConverter.GetBytes((uint)stripOffsets[d]),
            TagStripByteCounts => BitConverter.GetBytes((uint)this._strips[d].Length),
            _ => raw,
          };
          output.Write(BitConverter.GetBytes(tag));
          output.Write(BitConverter.GetBytes(type));
          output.Write(BitConverter.GetBytes(count));
          if (value.Length > 4) {
            output.Write(BitConverter.GetBytes((uint)valueAt));
            var padded = new byte[(value.Length + 1) & ~1];
            value.CopyTo(padded, 0);
            values[valueAt] = padded;
            valueAt += padded.Length;
          } else {
            var padded = new byte[4];
            value.CopyTo(padded, 0);
            output.Write(padded);
          }
        }

        output.Write(BitConverter.GetBytes((uint)(d + 1 < this._directories.Count ? directoryOffsets[d + 1] : 0)));
      }

      _Flush(output, values, stripArea);
      foreach (var data in this._strips)
        output.Write(data);
      return output.ToArray();
    }

    /// <summary>Writes the pending out-of-line values that come before <paramref name="until"/>.</summary>
    private static void _Flush(MemoryStream output, SortedDictionary<long, byte[]> values, long until) {
      while (output.Length < until && values.Remove(output.Length, out var block))
        output.Write(block);
      if (output.Length != until)
        throw new InvalidOperationException("LuraDocument directory layout is inconsistent.");
    }
  }
}
