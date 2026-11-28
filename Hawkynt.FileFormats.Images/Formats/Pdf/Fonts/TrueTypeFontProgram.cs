using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Pdf.Fonts;

/// <summary>A TrueType font program, as a PDF embeds it in a FontFile2 stream.</summary>
/// <remarks>
/// A PDF's embedded TrueType font is usually a subset a producer cut down for the document, and it
/// is often missing tables a standalone font must have, carries wrong checksums, or has a loca that
/// runs past glyf. None of that matters for drawing, so this reads only head, loca, glyf, hmtx,
/// hhea, maxp, cmap and post, checks nothing it does not need, and gives an empty outline for a
/// glyph whose data does not add up rather than refusing the font. The glyf decoding is converted
/// from pdf.js's <c>compileGlyf</c> in <c>font_renderer.js</c>, with composite placement by point
/// matching added where pdf.js leaves a TODO.
/// </remarks>
internal sealed class TrueTypeFontProgram : IPdfFontProgram {

  private const int _MaxCompositeDepth = 16;

  private readonly byte[] _data;
  private readonly int _glyfOffset;
  private readonly int _glyfLength;
  private readonly int[] _loca;
  private readonly double _scale;
  private readonly ushort[] _advances;
  private readonly short[] _sideBearings;
  private readonly Dictionary<(int Platform, int Encoding), int> _cmaps = [];
  private readonly Dictionary<int, SfntCmap?> _parsedCmaps = [];
  private readonly Dictionary<string, int> _gidByName = new(StringComparer.Ordinal);
  private readonly GlyphOutline?[] _outlines;

  private TrueTypeFontProgram(byte[] data, Dictionary<string, (int Offset, int Length)> tables) {
    this._data = data;

    var unitsPerEm = 1000;
    var longLoca = false;
    if (tables.TryGetValue("head", out var head) && head.Length >= 54) {
      unitsPerEm = _U16(data, head.Offset + 18);
      longLoca = _U16(data, head.Offset + 50) != 0;
    }
    if (unitsPerEm is < 16 or > 16384)
      unitsPerEm = 1000;
    this._scale = 1.0 / unitsPerEm;

    if (tables.TryGetValue("glyf", out var glyf)) {
      this._glyfOffset = glyf.Offset;
      this._glyfLength = glyf.Length;
    }

    var glyphCount = 0;
    if (tables.TryGetValue("maxp", out var maxp) && maxp.Length >= 6)
      glyphCount = _U16(data, maxp.Offset + 4);

    this._loca = tables.TryGetValue("loca", out var loca) ? _ReadLoca(data, loca, longLoca, glyphCount) : [0];
    if (glyphCount == 0 || glyphCount > this._loca.Length - 1)
      glyphCount = Math.Max(this._loca.Length - 1, 0);
    this.GlyphCount = glyphCount;

    this._advances = _ReadAdvances(data, tables, glyphCount);
    this._sideBearings = _ReadSideBearings(data, tables, glyphCount);
    this._outlines = new GlyphOutline?[glyphCount];

    if (tables.TryGetValue("cmap", out var cmap))
      this._ReadCmapDirectory(cmap);

    if (tables.TryGetValue("post", out var post))
      this._ReadPostNames(post);
  }

  /// <inheritdoc/>
  public int GlyphCount { get; }

  /// <inheritdoc/>
  public IReadOnlyDictionary<int, string>? BuiltInEncoding => null;

  /// <summary>Reads a TrueType program.</summary>
  /// <exception cref="InvalidDataException">The data holds no table directory.</exception>
  public static TrueTypeFontProgram Parse(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    var tables = ReadDirectory(data);
    if (!tables.ContainsKey("glyf") && tables.ContainsKey("CFF "))
      throw new InvalidDataException("An OpenType font with CFF outlines is not a TrueType program; use ParseOpenType.");

    return new(data, tables);
  }

  /// <summary>Reads an sfnt font of either kind: TrueType outlines, or CFF ones in an OpenType wrapper.</summary>
  public static IPdfFontProgram ParseOpenType(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    var tables = ReadDirectory(data);
    if (!tables.ContainsKey("glyf") && tables.TryGetValue("CFF ", out var cff))
      return CffFontProgram.Parse(data.AsSpan(cff.Offset, cff.Length).ToArray());

    return new TrueTypeFontProgram(data, tables);
  }

  /// <summary>
  /// Reads the TrueType outlines of a face whose table directory has been read already, such as
  /// one face of a collection.
  /// </summary>
  internal static TrueTypeFontProgram FromTables(byte[] data, Dictionary<string, (int Offset, int Length)> tables) => new(data, tables);

  /// <summary>Whether the font has a cmap subtable for a platform and encoding.</summary>
  public bool HasCmap(int platformId, int encodingId) => this._cmaps.ContainsKey((platformId, encodingId));

  /// <summary>The glyph id a cmap subtable gives a character code, or -1 when it gives none.</summary>
  public int CmapLookup(int platformId, int encodingId, int code) {
    if (!this._cmaps.TryGetValue((platformId, encodingId), out var offset))
      return -1;

    if (!this._parsedCmaps.TryGetValue(offset, out var cmap)) {
      try {
        cmap = SfntCmap.Read(this._data, offset);
      } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException) {
        cmap = null;
      }
      this._parsedCmaps[offset] = cmap;
    }

    return cmap?.Lookup(code) ?? -1;
  }

  /// <inheritdoc/>
  public int GlyphIdOfName(string name) => this._gidByName.TryGetValue(name, out var gid) ? gid : -1;

  /// <inheritdoc/>
  public double AdvanceWidth(int glyphId) {
    if ((uint)glyphId >= (uint)this.GlyphCount || this._advances.Length == 0)
      return double.NaN;

    // hmtx may list fewer advances than glyphs; the last one then applies to the rest.
    var advance = glyphId < this._advances.Length ? this._advances[glyphId] : this._advances[^1];
    return advance * this._scale;
  }

  /// <inheritdoc/>
  public GlyphOutline? Outline(int glyphId) {
    if ((uint)glyphId >= (uint)this.GlyphCount)
      return null;

    if (this._outlines[glyphId] is { } cached)
      return cached;

    var points = new List<_Point>();
    var contourEnds = new List<int>();
    try {
      this._Collect(glyphId, points, contourEnds, 0);
    } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException) {
      points.Clear();
      contourEnds.Clear();
    }

    // The glyph's origin is the phantom point hmtx's sidebearing puts left of xMin, not the one
    // glyf's coordinates imply; FreeType moves the outline by the difference and so does this.
    if (points.Count > 0) {
      var shift = this._PhantomShift(glyphId, 0);
      if (shift != 0)
        for (var p = 0; p < points.Count; ++p)
          points[p] = points[p] with { X = points[p].X + shift };
    }

    var outline = _Build(points, contourEnds, this._scale);
    this._outlines[glyphId] = outline;
    return outline;
  }

  // ---------------------------------------------------------------------------------------------
  // glyf
  // ---------------------------------------------------------------------------------------------

  private readonly record struct _Point(double X, double Y, bool OnCurve);

  private (int Start, int Length) _GlyphSpan(int glyphId) {
    if (glyphId + 1 >= this._loca.Length)
      return (0, 0);

    var start = this._loca[glyphId];
    var end = this._loca[glyphId + 1];
    if (end <= start || start >= this._glyfLength)
      return (0, 0);

    end = Math.Min(end, this._glyfLength);
    return (this._glyfOffset + start, end - start);
  }

  // How far the outline moves so its origin is the left phantom point: hmtx's sidebearing less the
  // header's xMin, taken from the component flagged USE_MY_METRICS when a composite has one, which
  // is where FreeType (and so poppler) takes a composite's phantom points from.
  private int _PhantomShift(int glyphId, int depth) {
    var (start, length) = this._GlyphSpan(glyphId);
    if (length < 10 || depth > _MaxCompositeDepth)
      return 0;

    var data = this._data;
    if ((short)_U16(data, start) < 0) {
      var i = start + 10;
      var end = start + length;
      int flags;
      do {
        flags = _U16(data, i);
        var component = _U16(data, i + 2);
        if ((flags & 0x200) != 0)
          return this._PhantomShift(component, depth + 1);

        i += 4 + ((flags & 0x01) != 0 ? 4 : 2) + ((flags & 0x08) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
      } while ((flags & 0x20) != 0 && i + 4 <= end);
    }

    return glyphId < this._sideBearings.Length ? this._sideBearings[glyphId] - (short)_U16(data, start + 2) : 0;
  }

  // Gathers a glyph's points in font units, composites resolved, so point matching can see them.
  private void _Collect(int glyphId, List<_Point> points, List<int> contourEnds, int depth) {
    if (depth > _MaxCompositeDepth)
      return;

    var (start, length) = this._GlyphSpan(glyphId);
    if (length < 10)
      return;

    var data = this._data;
    var end = start + length;
    var contours = (short)_U16(data, start);
    var i = start + 10;

    if (contours >= 0) {
      this._Simple(data, i, end, contours, points, contourEnds);
      return;
    }

    int flags;
    do {
      flags = _U16(data, i);
      var component = _U16(data, i + 2);
      i += 4;

      int arg1, arg2;
      if ((flags & 0x01) != 0) {
        arg1 = (flags & 0x02) != 0 ? (short)_U16(data, i) : _U16(data, i);
        arg2 = (flags & 0x02) != 0 ? (short)_U16(data, i + 2) : _U16(data, i + 2);
        i += 4;
      } else {
        arg1 = (flags & 0x02) != 0 ? (sbyte)data[i] : data[i];
        arg2 = (flags & 0x02) != 0 ? (sbyte)data[i + 1] : data[i + 1];
        i += 2;
      }

      double a = 1, b = 0, c = 0, d = 1;
      if ((flags & 0x08) != 0) {
        a = d = _F2Dot14(data, i);
        i += 2;
      } else if ((flags & 0x40) != 0) {
        a = _F2Dot14(data, i);
        d = _F2Dot14(data, i + 2);
        i += 4;
      } else if ((flags & 0x80) != 0) {
        a = _F2Dot14(data, i);
        b = _F2Dot14(data, i + 2);
        c = _F2Dot14(data, i + 4);
        d = _F2Dot14(data, i + 6);
        i += 8;
      }

      var firstPoint = points.Count;
      this._Collect(component, points, contourEnds, depth + 1);

      for (var p = firstPoint; p < points.Count; ++p) {
        var point = points[p];
        points[p] = point with { X = a * point.X + c * point.Y, Y = b * point.X + d * point.Y };
      }

      double dx, dy;
      if ((flags & 0x02) != 0) {
        dx = arg1;
        dy = arg2;
      } else {
        // Point matching: the component moves so that its point arg2 lands on the parent's arg1.
        var parentPoint = arg1;
        var childPoint = firstPoint + arg2;
        if ((uint)parentPoint < (uint)firstPoint && (uint)childPoint < (uint)points.Count) {
          dx = points[parentPoint].X - points[childPoint].X;
          dy = points[parentPoint].Y - points[childPoint].Y;
        } else
          dx = dy = 0;
      }

      if (dx != 0 || dy != 0)
        for (var p = firstPoint; p < points.Count; ++p) {
          var point = points[p];
          points[p] = point with { X = point.X + dx, Y = point.Y + dy };
        }
    } while ((flags & 0x20) != 0 && i + 4 <= end);
  }

  private void _Simple(byte[] data, int i, int end, int contours, List<_Point> points, List<int> contourEnds) {
    if (contours == 0)
      return;

    var baseIndex = points.Count;
    var ends = new int[contours];
    for (var k = 0; k < contours; ++k) {
      ends[k] = _U16(data, i);
      i += 2;
    }

    var pointCount = ends[^1] + 1;
    var instructionLength = _U16(data, i);
    i += 2 + instructionLength;

    var flags = new byte[pointCount];
    for (var p = 0; p < pointCount;) {
      var flag = data[i++];
      var repeat = 1;
      if ((flag & 0x08) != 0)
        repeat += data[i++];
      while (repeat-- > 0 && p < pointCount)
        flags[p++] = flag;
    }

    var xs = new int[pointCount];
    var x = 0;
    for (var p = 0; p < pointCount; ++p) {
      switch (flags[p] & 0x12) {
        case 0x00:
          x += (short)_U16(data, i);
          i += 2;
          break;
        case 0x02:
          x -= data[i++];
          break;
        case 0x12:
          x += data[i++];
          break;
      }
      xs[p] = x;
    }

    var y = 0;
    for (var p = 0; p < pointCount; ++p) {
      switch (flags[p] & 0x24) {
        case 0x00:
          y += (short)_U16(data, i);
          i += 2;
          break;
        case 0x04:
          y -= data[i++];
          break;
        case 0x24:
          y += data[i++];
          break;
      }
      points.Add(new(xs[p], y, (flags[p] & 1) != 0));
    }

    if (i > end)
      throw new InvalidDataException("Glyph data runs past its loca entry.");

    var previous = -1;
    foreach (var e in ends) {
      // Contour ends must rise; a broken one is clamped into range rather than trusted.
      var clamped = Math.Clamp(e, previous + 1, pointCount - 1);
      contourEnds.Add(baseIndex + clamped);
      previous = clamped;
    }
  }

  private static GlyphOutline _Build(List<_Point> points, List<int> contourEnds, double scale) {
    var outline = new GlyphOutline();
    var start = 0;
    var contour = new List<_Point>();
    foreach (var end in contourEnds) {
      if (end < start || end >= points.Count) {
        start = end + 1;
        continue;
      }

      contour.Clear();
      for (var p = start; p <= end; ++p)
        contour.Add(points[p]);
      start = end + 1;

      // As pdf.js: begin on an on-curve point, borrowing the last one or making the midpoint
      // between two off-curve ends when neither end is on the curve.
      if (contour[0].OnCurve)
        contour.Add(contour[0]);
      else if (contour[^1].OnCurve)
        contour.Insert(0, contour[^1]);
      else {
        var mid = new _Point((contour[0].X + contour[^1].X) / 2, (contour[0].Y + contour[^1].Y) / 2, true);
        contour.Insert(0, mid);
        contour.Add(mid);
      }

      outline.MoveTo(contour[0].X * scale, contour[0].Y * scale);
      for (var j = 1; j < contour.Count; ++j) {
        var p = contour[j];
        if (p.OnCurve) {
          outline.LineTo(p.X * scale, p.Y * scale);
          continue;
        }

        if (j + 1 >= contour.Count)
          break;

        var next = contour[j + 1];
        if (next.OnCurve) {
          outline.QuadTo(p.X * scale, p.Y * scale, next.X * scale, next.Y * scale);
          ++j;
        } else
          outline.QuadTo(p.X * scale, p.Y * scale, (p.X + next.X) / 2 * scale, (p.Y + next.Y) / 2 * scale);
      }

      outline.Close();
    }

    return outline;
  }

  // ---------------------------------------------------------------------------------------------
  // Tables
  // ---------------------------------------------------------------------------------------------

  /// <summary>
  /// Reads the table directory that starts at an offset into the data: 0 for a single font, a
  /// face's offset in a collection. Table offsets count from the start of the data either way.
  /// </summary>
  internal static Dictionary<string, (int Offset, int Length)> ReadDirectory(byte[] data, int at = 0) {
    if (at < 0 || data.Length - at < 12)
      throw new InvalidDataException("Data too small for a TrueType font.");

    var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
    var count = _U16(data, at + 4);
    for (var i = 0; i < count; ++i) {
      var record = at + 12 + i * 16;
      if (record + 16 > data.Length)
        break;

      var tag = Encoding.ASCII.GetString(data, record, 4);
      var offset = (long)_U32(data, record + 8);
      var length = (long)_U32(data, record + 12);
      if (offset >= data.Length)
        continue;

      // A table claiming more than the file has is cut to what is there; subsets do this.
      length = Math.Min(length, data.Length - offset);
      tables.TryAdd(tag, ((int)offset, (int)length));
    }

    if (tables.Count == 0)
      throw new InvalidDataException("TrueType font has no tables.");

    return tables;
  }

  private static int[] _ReadLoca(byte[] data, (int Offset, int Length) loca, bool longOffsets, int glyphCount) {
    var size = longOffsets ? 4 : 2;
    var entries = loca.Length / size;
    if (glyphCount > 0)
      entries = Math.Min(entries, glyphCount + 1);

    var offsets = new int[Math.Max(entries, 1)];
    for (var i = 0; i < entries; ++i) {
      var at = loca.Offset + i * size;
      offsets[i] = longOffsets ? (int)Math.Min(_U32(data, at), int.MaxValue) : _U16(data, at) * 2;
    }

    return offsets;
  }

  private static ushort[] _ReadAdvances(byte[] data, Dictionary<string, (int Offset, int Length)> tables, int glyphCount) {
    if (!tables.TryGetValue("hhea", out var hhea) || hhea.Length < 36 || !tables.TryGetValue("hmtx", out var hmtx))
      return [];

    int count = _U16(data, hhea.Offset + 34);
    count = Math.Min(count, hmtx.Length / 4);
    if (glyphCount > 0)
      count = Math.Min(count, glyphCount);

    var advances = new ushort[count];
    for (var i = 0; i < count; ++i)
      advances[i] = _U16(data, hmtx.Offset + i * 4);

    return advances;
  }

  private static short[] _ReadSideBearings(byte[] data, Dictionary<string, (int Offset, int Length)> tables, int glyphCount) {
    if (!tables.TryGetValue("hhea", out var hhea) || hhea.Length < 36 || !tables.TryGetValue("hmtx", out var hmtx))
      return [];

    // Long metrics carry an advance and a sidebearing; the glyphs after them carry the sidebearing only.
    int longCount = _U16(data, hhea.Offset + 34);
    var bearings = new short[glyphCount];
    var read = 0;
    for (var gid = 0; gid < glyphCount; ++gid) {
      var at = gid < longCount ? gid * 4 + 2 : longCount * 4 + (gid - longCount) * 2;
      if (at + 2 > hmtx.Length)
        break;
      bearings[gid] = (short)_U16(data, hmtx.Offset + at);
      read = gid + 1;
    }

    return read == glyphCount ? bearings : bearings[..read];
  }

  private void _ReadCmapDirectory((int Offset, int Length) cmap) {
    var data = this._data;
    if (cmap.Length < 4)
      return;

    var count = _U16(data, cmap.Offset + 2);
    for (var i = 0; i < count; ++i) {
      var record = cmap.Offset + 4 + i * 8;
      if (record + 8 > cmap.Offset + cmap.Length)
        break;

      var platform = _U16(data, record);
      var encoding = _U16(data, record + 2);
      var offset = (long)cmap.Offset + _U32(data, record + 4);
      if (offset + 4 <= data.Length)
        this._cmaps.TryAdd((platform, encoding), (int)offset);
    }
  }

  private void _ReadPostNames((int Offset, int Length) post) {
    var data = this._data;
    if (post.Length < 32)
      return;

    var version = _U32(data, post.Offset);
    try {
      switch (version) {
        case 0x00010000:
          for (var gid = 0; gid < Math.Min(this.GlyphCount, TrueTypeMacGlyphNames.Names.Length); ++gid)
            this._gidByName.TryAdd(TrueTypeMacGlyphNames.Names[gid], gid);
          break;

        case 0x00020000: {
          var end = post.Offset + post.Length;
          var count = _U16(data, post.Offset + 32);
          var indexAt = post.Offset + 34;
          var namesAt = indexAt + count * 2;
          var custom = new List<string>();
          for (var p = namesAt; p < end;) {
            var length = data[p++];
            if (p + length > end)
              break;
            custom.Add(Encoding.Latin1.GetString(data, p, length));
            p += length;
          }

          for (var gid = 0; gid < count; ++gid) {
            var index = _U16(data, indexAt + gid * 2);
            var name = index < 258 ? TrueTypeMacGlyphNames.Names[index] : index - 258 < custom.Count ? custom[index - 258] : null;
            if (name != null)
              this._gidByName.TryAdd(name, gid);
          }
          break;
        }

        // Format 2.5 is deprecated and 3 carries no names.
      }
    } catch (IndexOutOfRangeException) {
      // A post table that runs out gives the names read so far.
    }
  }

  private static double _F2Dot14(byte[] data, int at) => (short)_U16(data, at) / 16384.0;

  private static ushort _U16(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);

  private static uint _U32(byte[] data, int at) => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
