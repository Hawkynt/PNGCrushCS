using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.TrueType;

/// <summary>Reads a TrueType font's table directory and turns its glyphs into outlines.</summary>
/// <remarks>
/// Every number in a font is stored most significant byte first. The directory is checked before
/// anything is read out of it — a table whose offset and length run past the end of the file, a
/// <c>head</c> without its magic number, a <c>loca</c> that is not one entry longer than the glyph
/// count, or offsets in it that go backwards, are all a file that is not the font it says it is.
/// </remarks>
public static class TrueTypeReader {

  /// <summary>How many tables a font may declare.</summary>
  private const int _MaxTables = 512;

  /// <summary>How many glyphs a font may hold, the index into <c>loca</c> being sixteen bits.</summary>
  private const int _MaxGlyphs = 0xFFFF;

  /// <summary>How many points one glyph may have.</summary>
  private const int _MaxPoints = 1 << 16;

  /// <summary>How deep one composite glyph may reach into others.</summary>
  private const int _MaxComponentDepth = 8;

  /// <summary>Component flags, from the composite glyph description.</summary>
  private const int _ArgsAreWords = 0x0001, _ArgsAreXyValues = 0x0002, _HaveScale = 0x0008;
  private const int _MoreComponents = 0x0020, _HaveXAndYScale = 0x0040, _HaveTwoByTwo = 0x0080;

  /// <summary>Simple glyph flags, from the simple glyph description.</summary>
  private const int _OnCurve = 0x01, _XShort = 0x02, _YShort = 0x04, _Repeat = 0x08;
  private const int _XSameOrPositive = 0x10, _YSameOrPositive = 0x20;

  public static TrueTypeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TrueType font not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TrueTypeFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static TrueTypeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static TrueTypeFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12)
      throw new InvalidDataException("A TrueType font is too short to hold a table directory.");

    var version = _U32(data, 0);
    switch (version) {
      case TrueTypeFile.OpenTypeCffTag:
        throw new InvalidDataException("This font keeps its outlines in a CFF table as Type 2 charstrings, which is not the glyph format read here.");

      case TrueTypeFile.CollectionTag:
        throw new InvalidDataException("This is a TrueType collection, which is several fonts in one file rather than one font.");

      case TrueTypeFile.TrueTypeVersion or TrueTypeFile.AppleTag:
        break;

      default:
        throw new InvalidDataException($"A version of 0x{version:X8} is not one a TrueType font states.");
    }

    var tableCount = _U16(data, 4);
    if (tableCount is 0 or > _MaxTables)
      throw new InvalidDataException($"A font of {tableCount} tables is not one this reads.");

    var tables = _Directory(data, tableCount);

    var head = _Table(tables, "head", data);
    if (head.Length < 54)
      throw new InvalidDataException($"A head table of {head.Length} bytes is shorter than the fifty-four it is defined as.");

    if (_U32(head, 12) != TrueTypeFile.HeadMagic)
      throw new InvalidDataException("The head table does not carry its magic number, so this is not the table it claims to be.");

    var unitsPerEm = _U16(head, 18);
    if (unitsPerEm is < 16 or > 16384)
      throw new InvalidDataException($"A font of {unitsPerEm} units to the em is outside the sixteen to sixteen thousand the specification allows.");

    var longOffsets = (short)_U16(head, 50);
    if (longOffsets is not (0 or 1))
      throw new InvalidDataException($"The head table says its glyph offsets are stored in format {longOffsets}, which is neither short nor long.");

    var maxp = _Table(tables, "maxp", data);
    if (maxp.Length < 6)
      throw new InvalidDataException($"A maxp table of {maxp.Length} bytes cannot hold a glyph count.");

    var glyphCount = _U16(maxp, 4);
    if (glyphCount is 0 or > _MaxGlyphs)
      throw new InvalidDataException($"A font of {glyphCount} glyphs is not one this reads.");

    var loca = _Table(tables, "loca", data);
    var glyf = _Table(tables, "glyf", data);
    var offsets = _Offsets(loca, glyf, glyphCount, longOffsets == 1);

    var glyphs = new List<TrueTypeGlyph>(glyphCount);
    for (var index = 0; index < glyphCount; ++index)
      glyphs.Add(new(_Glyph(glyf, offsets, index, 0)));

    return new() { UnitsPerEm = unitsPerEm, GlyphCount = glyphCount, Glyphs = glyphs };
  }

  /// <summary>Reads the table records and checks each one lies inside the file.</summary>
  private static Dictionary<string, (int Offset, int Length)> _Directory(ReadOnlySpan<byte> data, int tableCount) {
    var end = 12 + tableCount * 16;
    if (end > data.Length)
      throw new InvalidDataException($"A directory of {tableCount} tables needs {end} bytes and the file has {data.Length}.");

    var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
    for (var i = 0; i < tableCount; ++i) {
      var record = 12 + i * 16;
      var tag = Encoding.ASCII.GetString(data.Slice(record, 4));
      var offset = _U32(data, record + 8);
      var length = _U32(data, record + 12);

      // A table that runs past the end of the file is the clearest sign the directory was written
      // for a file other than this one, or that this one has been cut.
      if (offset > (uint)data.Length || length > (uint)data.Length - offset)
        throw new InvalidDataException($"The {tag} table is stated at {offset} for {length} bytes, past the end of a file of {data.Length}.");

      tables[tag] = ((int)offset, (int)length);
    }

    return tables;
  }

  private static ReadOnlySpan<byte> _Table(Dictionary<string, (int Offset, int Length)> tables, string tag, ReadOnlySpan<byte> data) {
    if (!tables.TryGetValue(tag, out var table))
      throw new InvalidDataException($"A font with outlines has a {tag} table, and this one has none.");

    return data.Slice(table.Offset, table.Length);
  }

  /// <summary>
  /// Reads where each glyph starts.
  /// </summary>
  /// <remarks>
  /// <c>loca</c> holds one entry more than there are glyphs, the last being where the last glyph
  /// ends. In the short format the value stored is the offset divided by two. The entries have to
  /// run forwards, because an entry that goes backwards would make a glyph of negative length.
  /// </remarks>
  private static int[] _Offsets(ReadOnlySpan<byte> loca, ReadOnlySpan<byte> glyf, int glyphCount, bool longOffsets) {
    var entries = glyphCount + 1;
    var needed = entries * (longOffsets ? 4 : 2);
    if (loca.Length < needed)
      throw new InvalidDataException($"A loca table for {glyphCount} glyphs needs {needed} bytes and this one has {loca.Length}.");

    var offsets = new int[entries];
    for (var i = 0; i < entries; ++i) {
      offsets[i] = longOffsets ? (int)_U32(loca, i * 4) : _U16(loca, i * 2) * 2;
      if (i > 0 && offsets[i] < offsets[i - 1])
        throw new InvalidDataException($"Glyph {i - 1} starts at {offsets[i - 1]} and ends at {offsets[i]}, which is before it began.");
    }

    if (offsets[^1] > glyf.Length)
      throw new InvalidDataException($"The glyphs run to {offsets[^1]} and the glyf table is {glyf.Length} bytes.");

    return offsets;
  }

  /// <summary>Reads one glyph's contours, following a composite into the glyphs it is built from.</summary>
  private static List<IReadOnlyList<TrueTypePoint>> _Glyph(ReadOnlySpan<byte> glyf, int[] offsets, int index, int depth) {
    var contours = new List<IReadOnlyList<TrueTypePoint>>();
    if (index < 0 || index + 1 >= offsets.Length)
      throw new InvalidDataException($"A glyph numbered {index} is outside the {offsets.Length - 1} the font holds.");

    var start = offsets[index];
    var length = offsets[index + 1] - start;

    // An empty glyph is how a space is stored, and it is not an error.
    if (length == 0)
      return contours;

    if (length < 10)
      throw new InvalidDataException($"Glyph {index} is {length} bytes, shorter than the ten its header takes.");

    var glyph = glyf.Slice(start, length);
    var contourCount = (short)_U16(glyph, 0);

    return contourCount >= 0
      ? _Simple(glyph, contourCount, index)
      : _Composite(glyf, offsets, glyph, index, depth);
  }

  private static List<IReadOnlyList<TrueTypePoint>> _Simple(ReadOnlySpan<byte> glyph, int contourCount, int index) {
    var contours = new List<IReadOnlyList<TrueTypePoint>>(contourCount);
    if (contourCount == 0)
      return contours;

    var at = 10;
    if (at + contourCount * 2 + 2 > glyph.Length)
      throw new InvalidDataException($"Glyph {index} states {contourCount} contours and is too short to list where they end.");

    var ends = new int[contourCount];
    for (var i = 0; i < contourCount; ++i) {
      ends[i] = _U16(glyph, at + i * 2);

      // The end points run up the point list, so each has to be past the one before it.
      if (i > 0 && ends[i] <= ends[i - 1])
        throw new InvalidDataException($"Contour {i} of glyph {index} ends at point {ends[i]}, which is not after the {ends[i - 1]} the one before it ended at.");
    }

    at += contourCount * 2;
    var pointCount = ends[^1] + 1;
    if (pointCount is < 1 or > _MaxPoints)
      throw new InvalidDataException($"Glyph {index} has {pointCount} points, which is more than a glyph holds.");

    var instructions = _U16(glyph, at);
    at += 2 + instructions;
    if (at > glyph.Length)
      throw new InvalidDataException($"Glyph {index} states {instructions} bytes of instructions and runs off its own end.");

    // The flags are run-length coded: a flag with the repeat bit set is followed by how many more
    // points share it.
    var flags = new byte[pointCount];
    for (var i = 0; i < pointCount;) {
      if (at >= glyph.Length)
        throw new InvalidDataException($"Glyph {index} runs out of flags {i} points into {pointCount}.");

      var flag = glyph[at++];
      flags[i++] = flag;
      if ((flag & _Repeat) == 0)
        continue;

      if (at >= glyph.Length)
        throw new InvalidDataException($"Glyph {index} ends with a repeated flag and no count after it.");

      var repeats = glyph[at++];
      for (var r = 0; r < repeats && i < pointCount; ++r)
        flags[i++] = flag;
    }

    var xs = _Coordinates(glyph, ref at, flags, pointCount, index, _XShort, _XSameOrPositive);
    var ys = _Coordinates(glyph, ref at, flags, pointCount, index, _YShort, _YSameOrPositive);

    var from = 0;
    foreach (var end in ends) {
      var points = new List<TrueTypePoint>(end - from + 1);
      for (var i = from; i <= end; ++i)
        points.Add(new(xs[i], ys[i], (flags[i] & _OnCurve) != 0));

      contours.Add(points);
      from = end + 1;
    }

    return contours;
  }

  /// <summary>
  /// Reads one axis of a glyph's points, which are stored as steps from the point before.
  /// </summary>
  /// <remarks>
  /// The short bit makes the step one unit wide, and then the same-or-positive bit is its sign. With
  /// the short bit clear, that same bit instead means the point does not move at all, and only when
  /// both are clear is there a signed sixteen-bit step to read.
  /// </remarks>
  private static int[] _Coordinates(ReadOnlySpan<byte> glyph, ref int at, byte[] flags, int pointCount, int index, int shortBit, int sameBit) {
    var values = new int[pointCount];
    var value = 0;
    for (var i = 0; i < pointCount; ++i) {
      var flag = flags[i];
      if ((flag & shortBit) != 0) {
        if (at >= glyph.Length)
          throw new InvalidDataException($"Glyph {index} runs out of coordinates {i} points into {pointCount}.");

        var step = glyph[at++];
        value += (flag & sameBit) != 0 ? step : -step;
      } else if ((flag & sameBit) == 0) {
        if (at + 2 > glyph.Length)
          throw new InvalidDataException($"Glyph {index} runs out of coordinates {i} points into {pointCount}.");

        value += (short)_U16(glyph, at);
        at += 2;
      }

      values[i] = value;
    }

    return values;
  }

  /// <summary>Places the glyphs a composite is built from under their own offsets and transforms.</summary>
  private static List<IReadOnlyList<TrueTypePoint>> _Composite(ReadOnlySpan<byte> glyf, int[] offsets, ReadOnlySpan<byte> glyph, int index, int depth) {
    if (depth >= _MaxComponentDepth)
      throw new InvalidDataException($"A composite glyph nested more than {_MaxComponentDepth} deep, which a font is not.");

    var contours = new List<IReadOnlyList<TrueTypePoint>>();
    var at = 10;
    while (true) {
      if (at + 4 > glyph.Length)
        throw new InvalidDataException($"Composite glyph {index} ends in the middle of a component.");

      var flags = _U16(glyph, at);
      var component = _U16(glyph, at + 2);
      at += 4;

      double dx = 0, dy = 0;
      if ((flags & _ArgsAreWords) != 0) {
        if (at + 4 > glyph.Length)
          throw new InvalidDataException($"Composite glyph {index} ends in the middle of a component's arguments.");

        dx = (short)_U16(glyph, at);
        dy = (short)_U16(glyph, at + 2);
        at += 4;
      } else {
        if (at + 2 > glyph.Length)
          throw new InvalidDataException($"Composite glyph {index} ends in the middle of a component's arguments.");

        dx = (sbyte)glyph[at];
        dy = (sbyte)glyph[at + 1];
        at += 2;
      }

      // Arguments that are not xy values are point numbers, which match one point of what is placed
      // so far against one of the component. Nothing here does that, so the component is placed
      // where its own outline puts it rather than somewhere invented.
      if ((flags & _ArgsAreXyValues) == 0)
        dx = dy = 0;

      double a = 1, b = 0, c = 0, d = 1;
      if ((flags & _HaveScale) != 0) {
        a = d = _F2Dot14(glyph, ref at, index);
      } else if ((flags & _HaveXAndYScale) != 0) {
        a = _F2Dot14(glyph, ref at, index);
        d = _F2Dot14(glyph, ref at, index);
      } else if ((flags & _HaveTwoByTwo) != 0) {
        a = _F2Dot14(glyph, ref at, index);
        b = _F2Dot14(glyph, ref at, index);
        c = _F2Dot14(glyph, ref at, index);
        d = _F2Dot14(glyph, ref at, index);
      }

      foreach (var contour in _Glyph(glyf, offsets, component, depth + 1)) {
        var placed = new List<TrueTypePoint>(contour.Count);
        foreach (var point in contour)
          placed.Add(new(a * point.X + c * point.Y + dx, b * point.X + d * point.Y + dy, point.OnCurve));

        contours.Add(placed);
      }

      if ((flags & _MoreComponents) == 0)
        return contours;
    }
  }

  /// <summary>A number with two bits before the point and fourteen after it.</summary>
  private static double _F2Dot14(ReadOnlySpan<byte> glyph, ref int at, int index) {
    if (at + 2 > glyph.Length)
      throw new InvalidDataException($"Composite glyph {index} ends in the middle of a component's transform.");

    var raw = (short)_U16(glyph, at);
    at += 2;

    return raw / 16384.0;
  }

  private static ushort _U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

  private static uint _U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
}
