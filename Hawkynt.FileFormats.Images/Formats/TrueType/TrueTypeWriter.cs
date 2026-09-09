using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.TrueType;

/// <summary>Writes the outline model as an unhinted TrueType/OpenType font.</summary>
/// <remarks>
/// The writer follows Microsoft's OpenType specification for the sfnt directory and the
/// <c>head</c>, <c>hhea</c>, <c>hmtx</c>, <c>maxp</c>, <c>OS/2</c>, <c>cmap</c>, <c>name</c>,
/// <c>post</c>, <c>loca</c> and <c>glyf</c> tables. Outlines are written as simple glyphs; the
/// reader has already flattened composites into contours, so rebuilding composite structure would
/// invent information that is no longer present in <see cref="TrueTypeFile"/>.
/// <para/>
/// The in-memory model intentionally does not retain character mappings or font names. Glyph zero
/// therefore remains the required missing-glyph slot, while glyphs one onward are mapped in order
/// into the BMP private-use area for as far as that area has room. This makes a written font
/// deterministic without pretending the discarded source character mapping can be reconstructed.
/// </remarks>
public static class TrueTypeWriter {

  private const uint _FontChecksum = 0xB1B0AFBA;
  private const int _MaximumGlyphs = ushort.MaxValue;
  private const int _MaximumPoints = ushort.MaxValue;
  private const int _MaximumContours = short.MaxValue;
  private const int _PrivateUseStart = 0xE000;
  private const int _PrivateUseEnd = 0xF8FF;

  private readonly record struct _GlyphData(byte[] Bytes, short XMin, short YMin, short XMax, short YMax, int PointCount, int ContourCount) {
    public bool HasContours => this.ContourCount != 0;
  }

  private readonly record struct _Table(string Tag, byte[] Data);

  public static byte[] ToBytes(TrueTypeFile file) {
    var glyphs = _EncodeGlyphs(file, out var xMin, out var yMin, out var xMax, out var yMax, out var maxPoints, out var maxContours);
    var glyphCount = glyphs.Length;

    var glyf = new List<byte>();
    var loca = new byte[checked((glyphCount + 1) * 4)];
    for (var i = 0; i < glyphCount; ++i) {
      _U32(loca, i * 4, checked((uint)glyf.Count));
      glyf.AddRange(glyphs[i].Bytes);
      if ((glyf.Count & 1) != 0)
        glyf.Add(0);
    }

    _U32(loca, glyphCount * 4, checked((uint)glyf.Count));

    var mappedGlyphs = Math.Min(Math.Max(glyphCount - 1, 0), _PrivateUseEnd - _PrivateUseStart + 1);
    var tables = new[] {
      new _Table("OS/2", _Os2(file.UnitsPerEm, yMin, yMax, mappedGlyphs)),
      new _Table("cmap", _Cmap(mappedGlyphs)),
      new _Table("glyf", glyf.ToArray()),
      new _Table("head", _Head(file.UnitsPerEm, xMin, yMin, xMax, yMax)),
      new _Table("hhea", _Hhea(file.UnitsPerEm, glyphs, yMin, yMax)),
      new _Table("hmtx", _Hmtx(file.UnitsPerEm, glyphs)),
      new _Table("loca", loca),
      new _Table("maxp", _Maxp(glyphCount, maxPoints, maxContours)),
      new _Table("name", _Name()),
      new _Table("post", _Post(file.UnitsPerEm)),
    };

    Array.Sort(tables, static (left, right) => StringComparer.Ordinal.Compare(left.Tag, right.Tag));
    return _Sfnt(tables);
  }

  public static TrueTypeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), "A TrueType source image must have positive dimensions.");

    var largestDimension = Math.Max(image.Width, image.Height);
    if (largestDimension > 16384)
      throw new ArgumentOutOfRangeException(nameof(image), "A TrueType em may not exceed 16384 units, so neither source dimension may exceed 16384 pixels.");

    image = image.EnsureAnyFormat(PixelFormat.Rgb24);
    var contours = new List<IReadOnlyList<TrueTypePoint>>();
    var pointCount = 0;

    for (var y = 0; y < image.Height; ++y) {
      var x = 0;
      while (x < image.Width) {
        while (x < image.Width && !_IsInk(image, x, y))
          ++x;
        if (x == image.Width)
          break;

        var from = x++;
        while (x < image.Width && _IsInk(image, x, y))
          ++x;

        if (pointCount > _MaximumPoints - 4)
          throw new ArgumentException($"The raster needs more than {_MaximumPoints} outline points after run coalescing.", nameof(image));
        if (contours.Count == _MaximumContours)
          throw new ArgumentException($"The raster needs more than {_MaximumContours} contours after run coalescing.", nameof(image));

        var top = image.Height - y;
        var bottom = top - 1;
        contours.Add([
          new(from, top, true),
          new(from, bottom, true),
          new(x, bottom, true),
          new(x, top, true),
        ]);
        pointCount += 4;
      }
    }

    return new TrueTypeFile {
      UnitsPerEm = Math.Clamp(largestDimension, 16, 16384),
      GlyphCount = 1,
      Glyphs = [new TrueTypeGlyph(contours)],
    };
  }

  private static bool _IsInk(RawImage image, int x, int y) {
    var at = (y * image.Width + x) * 3;
    var pixels = image.PixelData;
    var luma = 299 * pixels[at] + 587 * pixels[at + 1] + 114 * pixels[at + 2];
    return luma < 128000;
  }

  private static _GlyphData[] _EncodeGlyphs(TrueTypeFile file, out short xMin, out short yMin, out short xMax, out short yMax, out int maxPoints, out int maxContours) {
    if (file.UnitsPerEm is < 16 or > 16384)
      throw new ArgumentOutOfRangeException(nameof(file), "TrueType unitsPerEm must be in the range 16..16384.");
    if (file.Glyphs is null)
      throw new ArgumentException("A TrueType font needs a glyph collection.", nameof(file));
    if (file.Glyphs.Count is < 1 or > _MaximumGlyphs)
      throw new ArgumentOutOfRangeException(nameof(file), $"A TrueType font must contain 1..{_MaximumGlyphs} glyphs.");
    if (file.GlyphCount != file.Glyphs.Count)
      throw new ArgumentException("TrueType GlyphCount must equal the number of glyphs in Glyphs.", nameof(file));

    var result = new _GlyphData[file.Glyphs.Count];
    var haveBounds = false;
    short globalXMin = 0, globalYMin = 0, globalXMax = 0, globalYMax = 0;
    maxPoints = 0;
    maxContours = 0;

    for (var i = 0; i < file.Glyphs.Count; ++i) {
      result[i] = _Glyph(file.Glyphs[i], i);
      var glyph = result[i];
      maxPoints = Math.Max(maxPoints, glyph.PointCount);
      maxContours = Math.Max(maxContours, glyph.ContourCount);
      if (!glyph.HasContours)
        continue;

      if (!haveBounds) {
        globalXMin = glyph.XMin;
        globalYMin = glyph.YMin;
        globalXMax = glyph.XMax;
        globalYMax = glyph.YMax;
        haveBounds = true;
      } else {
        globalXMin = Math.Min(globalXMin, glyph.XMin);
        globalYMin = Math.Min(globalYMin, glyph.YMin);
        globalXMax = Math.Max(globalXMax, glyph.XMax);
        globalYMax = Math.Max(globalYMax, glyph.YMax);
      }
    }

    xMin = globalXMin;
    yMin = globalYMin;
    xMax = globalXMax;
    yMax = globalYMax;
    return result;
  }

  private static _GlyphData _Glyph(TrueTypeGlyph glyph, int glyphIndex) {
    if (glyph.Contours is null)
      throw new ArgumentException($"TrueType glyph {glyphIndex} has no contour collection.", nameof(glyph));
    if (glyph.Contours.Count > _MaximumContours)
      throw new ArgumentException($"TrueType glyph {glyphIndex} has more than {_MaximumContours} contours.", nameof(glyph));
    if (glyph.Contours.Count == 0)
      return new([], 0, 0, 0, 0, 0, 0);

    var points = new List<(short X, short Y, bool OnCurve)>();
    var ends = new ushort[glyph.Contours.Count];
    var contourIndex = 0;
    foreach (var contour in glyph.Contours) {
      if (contour is null || contour.Count == 0)
        throw new ArgumentException($"TrueType glyph {glyphIndex} contains an empty contour.", nameof(glyph));
      if (points.Count > _MaximumPoints - contour.Count)
        throw new ArgumentException($"TrueType glyph {glyphIndex} has more than {_MaximumPoints} points.", nameof(glyph));

      foreach (var point in contour)
        points.Add((_FUnit(point.X, glyphIndex), _FUnit(point.Y, glyphIndex), point.OnCurve));
      ends[contourIndex++] = checked((ushort)(points.Count - 1));
    }

    var xMin = points.Min(static point => point.X);
    var yMin = points.Min(static point => point.Y);
    var xMax = points.Max(static point => point.X);
    var yMax = points.Max(static point => point.Y);

    var flags = new byte[points.Count];
    var xData = new List<byte>();
    var yData = new List<byte>();
    var previousX = 0;
    var previousY = 0;
    for (var i = 0; i < points.Count; ++i) {
      var point = points[i];
      var flag = point.OnCurve ? (byte)0x01 : (byte)0;
      flag |= _Coordinate(point.X - previousX, 0x02, 0x10, xData, glyphIndex);
      flag |= _Coordinate(point.Y - previousY, 0x04, 0x20, yData, glyphIndex);
      flags[i] = flag;
      previousX = point.X;
      previousY = point.Y;
    }

    var data = new List<byte>(10 + ends.Length * 2 + 2 + flags.Length + xData.Count + yData.Count);
    _I16(data, checked((short)glyph.Contours.Count));
    _I16(data, xMin);
    _I16(data, yMin);
    _I16(data, xMax);
    _I16(data, yMax);
    foreach (var end in ends)
      _U16(data, end);
    _U16(data, 0);
    data.AddRange(flags);
    data.AddRange(xData);
    data.AddRange(yData);

    return new(data.ToArray(), xMin, yMin, xMax, yMax, points.Count, glyph.Contours.Count);
  }

  private static short _FUnit(double value, int glyphIndex) {
    if (!double.IsFinite(value))
      throw new ArgumentException($"TrueType glyph {glyphIndex} contains a non-finite coordinate.");

    var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
    if (rounded is < short.MinValue or > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(value), $"TrueType glyph {glyphIndex} contains a coordinate outside the signed 16-bit FUnit range.");
    return (short)rounded;
  }

  private static byte _Coordinate(int delta, byte shortBit, byte sameOrPositiveBit, List<byte> data, int glyphIndex) {
    if (delta == 0)
      return sameOrPositiveBit;
    if (delta is >= -255 and <= 255) {
      data.Add((byte)Math.Abs(delta));
      return (byte)(shortBit | (delta > 0 ? sameOrPositiveBit : 0));
    }
    if (delta is < short.MinValue or > short.MaxValue)
      throw new ArgumentException($"TrueType glyph {glyphIndex} has consecutive points more than 32768 font units apart.");

    _I16(data, (short)delta);
    return 0;
  }

  private static byte[] _Head(int unitsPerEm, short xMin, short yMin, short xMax, short yMax) {
    var data = new byte[54];
    _U16(data, 0, 1);
    _U32(data, 4, 0x00010000);
    _U32(data, 12, TrueTypeFile.HeadMagic);
    _U16(data, 18, unitsPerEm);
    _I16(data, 36, xMin);
    _I16(data, 38, yMin);
    _I16(data, 40, xMax);
    _I16(data, 42, yMax);
    _U16(data, 46, 8);
    _I16(data, 48, 2);
    _I16(data, 50, 1);
    return data;
  }

  private static byte[] _Maxp(int glyphCount, int maxPoints, int maxContours) {
    var data = new byte[32];
    _U32(data, 0, 0x00010000);
    _U16(data, 4, glyphCount);
    _U16(data, 6, maxPoints);
    _U16(data, 8, maxContours);
    _U16(data, 14, 1);
    return data;
  }

  private static byte[] _Hhea(int unitsPerEm, _GlyphData[] glyphs, short yMin, short yMax) {
    var data = new byte[36];
    _U16(data, 0, 1);
    _I16(data, 4, yMax);
    _I16(data, 6, yMin);
    _U16(data, 10, unitsPerEm);
    _I16(data, 12, _ClampFWord(glyphs.Min(static glyph => (int)glyph.XMin)));
    _I16(data, 14, _ClampFWord(glyphs.Min(glyph => unitsPerEm - glyph.XMax)));
    _I16(data, 16, _ClampFWord(glyphs.Max(static glyph => (int)glyph.XMax)));
    _I16(data, 18, 1);
    _I16(data, 32, 0);
    _U16(data, 34, glyphs.Length);
    return data;
  }

  private static byte[] _Hmtx(int unitsPerEm, _GlyphData[] glyphs) {
    var data = new byte[checked(glyphs.Length * 4)];
    for (var i = 0; i < glyphs.Length; ++i) {
      _U16(data, i * 4, unitsPerEm);
      _I16(data, i * 4 + 2, glyphs[i].XMin);
    }
    return data;
  }

  private static byte[] _Os2(int unitsPerEm, short yMin, short yMax, int mappedGlyphs) {
    var data = new byte[78];
    _I16(data, 2, checked((short)unitsPerEm));
    _U16(data, 4, 400);
    _U16(data, 6, 5);
    Encoding.ASCII.GetBytes("HAWK").CopyTo(data, 58);
    _U16(data, 62, 0x0040);
    if (mappedGlyphs > 0) {
      _U16(data, 64, _PrivateUseStart);
      _U16(data, 66, _PrivateUseStart + mappedGlyphs - 1);
      _U32(data, 46, 1u << 28);
    }
    _I16(data, 68, yMax);
    _I16(data, 70, yMin);
    _U16(data, 74, Math.Max(0, (int)yMax));
    _U16(data, 76, Math.Max(0, -(int)yMin));
    return data;
  }

  private static byte[] _Cmap(int mappedGlyphs) {
    var segmentCount = mappedGlyphs > 0 ? 2 : 1;
    var subtableLength = 16 + segmentCount * 8;
    const int subtableOffset = 20;
    var data = new byte[subtableOffset + subtableLength];
    _U16(data, 2, 2);
    _U16(data, 6, 3);
    _U32(data, 8, subtableOffset);
    _U16(data, 12, 3);
    _U16(data, 14, 1);
    _U32(data, 16, subtableOffset);

    var at = subtableOffset;
    _U16(data, at, 4);
    _U16(data, at + 2, subtableLength);
    _U16(data, at + 6, segmentCount * 2);
    var power = _FloorPowerOfTwo(segmentCount);
    _U16(data, at + 8, power * 2);
    _U16(data, at + 10, _Log2(power));
    _U16(data, at + 12, segmentCount * 2 - power * 2);

    var endCodes = at + 14;
    var startCodes = endCodes + segmentCount * 2 + 2;
    var deltas = startCodes + segmentCount * 2;
    if (mappedGlyphs > 0) {
      var last = _PrivateUseStart + mappedGlyphs - 1;
      _U16(data, endCodes, last);
      _U16(data, startCodes, _PrivateUseStart);
      _U16(data, deltas, unchecked((ushort)(1 - _PrivateUseStart)));
    }

    var sentinel = segmentCount - 1;
    _U16(data, endCodes + sentinel * 2, ushort.MaxValue);
    _U16(data, startCodes + sentinel * 2, ushort.MaxValue);
    _U16(data, deltas + sentinel * 2, 1);
    return data;
  }

  private static byte[] _Name() {
    var names = new (ushort Id, string Value)[] {
      (1, "PNGCrushCS TrueType"),
      (2, "Regular"),
      (4, "PNGCrushCS TrueType Regular"),
      (6, "PNGCrushCS-TrueType-Regular"),
    };
    var encoded = names.Select(static name => Encoding.BigEndianUnicode.GetBytes(name.Value)).ToArray();
    var stringOffset = 6 + names.Length * 12;
    var data = new byte[stringOffset + encoded.Sum(static value => value.Length)];
    _U16(data, 2, names.Length);
    _U16(data, 4, stringOffset);

    var storageOffset = 0;
    for (var i = 0; i < names.Length; ++i) {
      var record = 6 + i * 12;
      _U16(data, record, 3);
      _U16(data, record + 2, 1);
      _U16(data, record + 4, 0x0409);
      _U16(data, record + 6, names[i].Id);
      _U16(data, record + 8, encoded[i].Length);
      _U16(data, record + 10, storageOffset);
      encoded[i].CopyTo(data, stringOffset + storageOffset);
      storageOffset += encoded[i].Length;
    }
    return data;
  }

  private static byte[] _Post(int unitsPerEm) {
    var data = new byte[32];
    _U32(data, 0, 0x00030000);
    _I16(data, 8, _ClampFWord(-unitsPerEm / 10));
    _I16(data, 10, _ClampFWord(Math.Max(1, unitsPerEm / 20)));
    return data;
  }

  private static byte[] _Sfnt(_Table[] tables) {
    var tableCount = tables.Length;
    var directoryLength = checked(12 + tableCount * 16);
    var offsets = new int[tableCount];
    var totalLength = directoryLength;
    for (var i = 0; i < tableCount; ++i) {
      totalLength = _Align4(totalLength);
      offsets[i] = totalLength;
      totalLength = checked(totalLength + tables[i].Data.Length);
    }

    totalLength = _Align4(totalLength);
    var font = new byte[totalLength];
    _U32(font, 0, TrueTypeFile.TrueTypeVersion);
    _U16(font, 4, tableCount);
    var power = _FloorPowerOfTwo(tableCount);
    _U16(font, 6, power * 16);
    _U16(font, 8, _Log2(power));
    _U16(font, 10, tableCount * 16 - power * 16);

    var headOffset = -1;
    for (var i = 0; i < tableCount; ++i) {
      var table = tables[i];
      var record = 12 + i * 16;
      Encoding.ASCII.GetBytes(table.Tag).CopyTo(font, record);
      _U32(font, record + 4, _Checksum(table.Data));
      _U32(font, record + 8, offsets[i]);
      _U32(font, record + 12, table.Data.Length);
      table.Data.CopyTo(font, offsets[i]);
      if (table.Tag == "head")
        headOffset = offsets[i];
    }

    if (headOffset < 0)
      throw new InvalidOperationException("The TrueType writer assembled a font without its head table.");
    _U32(font, headOffset + 8, unchecked(_FontChecksum - _Checksum(font)));
    return font;
  }

  private static uint _Checksum(ReadOnlySpan<byte> data) {
    var sum = 0u;
    var at = 0;
    for (; at + 4 <= data.Length; at += 4)
      sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4)));
    if (at == data.Length)
      return sum;

    uint tail = 0;
    for (var shift = 24; at < data.Length; shift -= 8, ++at)
      tail |= (uint)data[at] << shift;
    return unchecked(sum + tail);
  }

  private static short _ClampFWord(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
  private static int _Align4(int value) => checked((value + 3) & ~3);

  private static int _FloorPowerOfTwo(int value) {
    var power = 1;
    while (power <= value / 2)
      power <<= 1;
    return power;
  }

  private static ushort _Log2(int powerOfTwo) {
    ushort result = 0;
    for (; powerOfTwo > 1; powerOfTwo >>= 1)
      ++result;
    return result;
  }

  private static void _U16(List<byte> target, int value) {
    target.Add((byte)(value >> 8));
    target.Add((byte)value);
  }

  private static void _I16(List<byte> target, short value) => _U16(target, unchecked((ushort)value));
  private static void _U16(byte[] target, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset, 2), checked((ushort)value));
  private static void _I16(byte[] target, int offset, short value) => BinaryPrimitives.WriteInt16BigEndian(target.AsSpan(offset, 2), value);
  private static void _U32(byte[] target, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset, 4), value);
  private static void _U32(byte[] target, int offset, int value) => _U32(target, offset, checked((uint)value));
}
