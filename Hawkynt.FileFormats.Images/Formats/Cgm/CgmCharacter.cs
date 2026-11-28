using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Cgm;

/// <summary>
/// Reads the character encoding of a Computer Graphics Metafile (ISO/IEC 8632-2) by translating it
/// into the binary encoding's commands, which the renderer already draws.
/// </summary>
/// <remarks>
/// The character encoding is built for 7-bit channels. An element opens with its opcode: one byte
/// from 0x20 to 0x2F for the commonest primitives, two bytes (the first 0x30 to 0x3F) for the rest.
/// Its parameters follow as bytes from 0x40 to 0x7F, so the next opcode is simply the next byte
/// below 0x40, and an element carries no length.
/// <list type="bullet">
/// <item>An integer is a run of bytes: 0x20 says another byte follows, the first byte's 0x10 is the
/// sign, and the bits below carry the magnitude most significant first — four bits in the first
/// byte, five in each one after.</item>
/// <item>A real is such an integer times a power of two. Where the real precision allows an
/// exponent, the first byte gives one data bit to a flag (0x08) saying an exponent integer follows;
/// otherwise the power is the precision's default exponent.</item>
/// <item>A point list is incremental: each point is its difference from the one before, starting
/// from the origin, and in a list of reals the exponent carries from one point to the next.</item>
/// <item>A direct colour packs two bits of each of red, green and blue into a byte (0x20 and 0x04
/// red, 0x10 and 0x02 green, 0x08 and 0x01 blue), as many bytes as half the colour precision.</item>
/// <item>A colour list — a cell array's cells, a colour table's entries — opens with a byte naming
/// its coding: plain values, a bit stream of six bits a byte, value-and-count runs, or runs whose
/// values are bit streams.</item>
/// <item>A string is framed by ESC X and ESC \ (or their 8-bit forms 0x98 and 0x9C).</item>
/// <item>BEGIN METAFILE may list control characters a channel cannot carry; each such character is
/// then sent as ~ and the character 0x40 above it, ~&gt; standing for ~ itself.</item>
/// </list>
/// The translation writes the precisions every translated metafile shares (see
/// <see cref="CgmTranslation.Precisions"/>); every element that changes how a later one is read or
/// drawn is translated, and the others — text and fonts, markers, segments, escapes — are passed
/// over.
/// <para/>
/// Written from the standard's coding rules as RAL-CGM 3.50 reads and writes them; RAL-CGM is under
/// a non-commercial licence, so it is the oracle and not a source. Incremental Huffman-coded point
/// lists are refused, as RAL-CGM refuses them.
/// </remarks>
internal static class CgmCharacter {

  private const int _Eof = -1;
  private const int _StringStart = 0x1B58;
  private const int _StringEnd = 0x1B5C;

  /// <summary>The largest cell array the translation unpacks.</summary>
  private const long _MaximumCells = 1L << 26;

  /// <summary>Whether the data opens with the character encoding's BEGIN METAFILE opcode.</summary>
  /// <remarks>
  /// The opcode alone is two printable characters, so it must also be followed by what BEGIN
  /// METAFILE holds: substitution characters, then the ESC that frames the name.
  /// </remarks>
  internal static bool Matches(ReadOnlySpan<byte> data) {
    if (data.Length < 3 || (data[0] & 0x7F) != 0x30 || (data[1] & 0x7F) != 0x20)
      return false;
    var end = Math.Min(data.Length, 2 + 64);
    for (var i = 2; i < end; ++i) {
      if (data[i] is 0x1B or 0x98)
        return true;
      if (data[i] is < 0x20 or > 0x7E)
        return false;
    }

    return false;
  }

  internal static List<CgmCommand> Translate(ReadOnlySpan<byte> data) {
    var translator = new Translator(new Source(data.ToArray()));
    translator.Run();
    return translator.Commands;
  }

  // ============================================================================================
  // Bytes
  // ============================================================================================

  /// <summary>The metafile's bytes after character substitution, with room to put bytes back.</summary>
  private sealed class Source(byte[] data) {
    private int _at;
    private readonly Stack<int> _back = new();

    /// <summary>Which control characters BEGIN METAFILE asked to substitute: 0x00 to 0x20, and DEL at 0x21.</summary>
    internal readonly bool[] Substituted = new bool[0x22];

    internal bool Substituting;

    internal void ClearSubstitution() {
      Array.Clear(this.Substituted);
      this.Substituting = false;
    }

    internal void Unread(int b) {
      if (b != _Eof)
        this._back.Push(b);
    }

    internal int Next() {
      if (this._back.Count > 0)
        return this._back.Pop();

      var announced = false;
      while (true) {
        if (this._at >= data.Length)
          return _Eof;
        int c = data[this._at++];
        if (!this.Substituting) {
          // Line feeds and nulls a channel may have added carry nothing.
          if (c is 0x0A or 0x00)
            continue;
          return c;
        }

        if (c == 0x7E) {
          announced = true;
          continue;
        }

        // A character the metafile said it would substitute, arriving bare, was added on the way.
        if (c <= 0x20 && this.Substituted[c])
          continue;
        if (c == 0x7F && this.Substituted[0x21])
          continue;

        if (announced) {
          if (c is >= 0x40 and <= 0x60 && this.Substituted[c - 0x40])
            c -= 0x40;
          else if (c == 0x3F && this.Substituted[0x21])
            c = 0x7F;
          else if (c == 0x3E)
            c = 0x7E;
        }

        return c;
      }
    }

    /// <summary>The next element's opcode: one byte, or two when the first is 0x30 to 0x3F or ESC.</summary>
    internal int Opcode() {
      var c = this.Next();
      if (c == _Eof)
        return _Eof;
      c &= 0x7F;
      if ((c & 0xF0) == 0x30 || c == 0x1B) {
        var second = this.Next();
        if (second == _Eof)
          return _Eof;
        c = c << 8 | second;
      }

      return c;
    }
  }

  private static bool _IsParameter(int b) => b != _Eof && b != 0x98 && b != 0x9C && (b & 0x40) != 0;

  // ============================================================================================
  // Translation
  // ============================================================================================

  /// <summary>How a picture starts reading, as the metafile (and its defaults replacement) leaves it.</summary>
  private record struct PictureState(bool Direct, bool LineWidthAbsolute, bool EdgeWidthAbsolute, int VdcExponent, bool VdcExponentAllowed);

  private sealed class Translator(Source source) {
    internal readonly List<CgmCommand> Commands = [];

    // Metafile-wide reading state.
    private bool _vdcInteger = true;
    private int _realExponent = -10;
    private bool _realExponentAllowed;
    private int _colourPrecision = 6;
    private int _indexPrecision = 10;
    private int _colourBits = 8;
    private bool _extentStated;

    private PictureState _defaults = new(false, false, false, -10, false);
    private PictureState _picture = new(false, false, false, -10, false);
    private bool _ended;

    private void _Emit(int elementClass, int elementId, CgmCommandWriter? writer = null)
      => this.Commands.Add(new(elementClass, elementId, writer?.ToArray() ?? []));

    internal void Run() {
      var code = source.Opcode();
      if (code != 0x3020)
        throw new InvalidDataException("A character-encoded metafile opens with BEGIN METAFILE and this one does not.");

      while (code != _Eof) {
        if (code == _StringStart) {
          this._SkipString();
        } else if (code < 0x100 && (code & 0x40) != 0) {
          // A stray parameter byte between elements.
        } else {
          this._Element(code);
          if (this._ended)
            return;
        }

        code = source.Opcode();
      }

      throw new InvalidDataException("A character-encoded metafile ends with END METAFILE and this one does not.");
    }

    // ------------------------------------------------------------------------------------------
    // Parameters
    // ------------------------------------------------------------------------------------------

    private int _NextParameter(string element) {
      var b = source.Next();
      if (!_IsParameter(b))
        throw new InvalidDataException($"The character-encoded element {element} ends before a parameter it needs.");
      return b;
    }

    /// <summary>An integer; <paramref name="first"/> is its first byte when already read.</summary>
    private long _Integer(string element, int first = _Eof) => this._Integer(element, first, false, out _);

    private long _Integer(string element, int first, bool exponentAllowed, out bool exponentPresent) {
      var b = first == _Eof ? this._NextParameter(element) : first;
      var negative = (b & 0x10) != 0;
      exponentPresent = exponentAllowed && (b & 0x08) != 0;
      var bits = exponentAllowed ? 3 : 4;
      long value = 0;
      while (true) {
        value = value << bits | (long)(b & ((1 << bits) - 1));
        if (value > int.MaxValue)
          throw new InvalidDataException($"The character-encoded element {element} states an integer too large to hold.");
        bits = 5;
        if ((b & 0x20) == 0)
          break;
        b = this._NextParameter(element);
      }

      return negative ? -value : value;
    }

    private double _Real(string element, int first, int defaultExponent, bool exponentAllowed, out int exponent) {
      var mantissa = this._Integer(element, first, exponentAllowed, out var present);
      exponent = present ? checked((int)this._Integer(element)) : defaultExponent;
      return Math.ScaleB(mantissa, exponent);
    }

    private double _Real(string element) => this._Real(element, _Eof, this._realExponent, this._realExponentAllowed, out _);

    private double _VdcReal(string element) => this._Real(element, _Eof, this._picture.VdcExponent, this._picture.VdcExponentAllowed, out _);

    /// <summary>A coordinate, written out at the translation's own precision.</summary>
    private void _Vdc(string element, CgmCommandWriter w) {
      if (this._vdcInteger)
        w.Int32(this._Integer(element));
      else
        w.Double(this._VdcReal(element));
    }

    private (int Red, int Green, int Blue) _Direct(string element, int first, int precision) {
      int r = 0, g = 0, b = 0;
      for (var left = precision; left > 0; left -= 2) {
        var c = first == _Eof ? this._NextParameter(element) : first;
        first = _Eof;
        r = r << 2 | (c & 0x20) >> 4 | (c & 0x04) >> 2;
        g = g << 2 | (c & 0x10) >> 3 | (c & 0x02) >> 1;
        b = b << 2 | (c & 0x08) >> 2 | (c & 0x01);
      }

      // An odd precision leaves one bit too many in each component.
      if ((precision & 1) != 0) {
        r >>= 1;
        g >>= 1;
        b >>= 1;
      }

      return (r, g, b);
    }

    private void _WriteDirect(CgmCommandWriter w, (int Red, int Green, int Blue) colour) {
      w.UInt(colour.Red, this._colourBits);
      w.UInt(colour.Green, this._colourBits);
      w.UInt(colour.Blue, this._colourBits);
    }

    /// <summary>A colour in the picture's colour selection mode.</summary>
    private void _Colour(string element, CgmCommandWriter w) {
      if (this._picture.Direct)
        this._WriteDirect(w, this._Direct(element, _Eof, this._colourPrecision));
      else
        w.UInt(this._Integer(element), 32);
    }

    /// <summary>Whatever is left of the element: its unread parameters, strings among them.</summary>
    private void _SkipParameters() {
      while (true) {
        var c = source.Next();
        if (c == _Eof)
          return;
        if (c == 0x1B) {
          var d = source.Next();
          if (d == 0x58) {
            this._SkipString();
            continue;
          }

          source.Unread(d);
          source.Unread(c);
          return;
        }

        if (c == 0x98) {
          this._SkipString();
          continue;
        }

        if (!_IsParameter(c)) {
          source.Unread(c);
          return;
        }
      }
    }

    /// <summary>A string, its ESC X opener (if not already consumed) skipped, up to ESC \ or 0x9C.</summary>
    private string _String() {
      var text = new StringBuilder();
      while (true) {
        var c = source.Next();
        if (c == _Eof)
          throw new InvalidDataException("A character-encoded metafile ends inside a string.");
        if (c == 0x98)
          continue;
        if (c == 0x9C)
          break;
        if (c == 0x1B) {
          c = source.Next();
          if (c == 0x5C)
            break;
          if (c == 0x58)
            continue;
          if (c == _Eof)
            throw new InvalidDataException("A character-encoded metafile ends inside a string.");
        }

        text.Append((char)c);
      }

      return text.ToString();
    }

    private void _SkipString() => _ = this._String();

    /// <summary>
    /// The incremental point list of LINE, DISJOINT LINE, POLYGON and POLYGON SET, with each point's
    /// edge flag for the last.
    /// </summary>
    private void _Points(string element, CgmCommandWriter w, bool flags) {
      long ix = 0, iy = 0;
      double rx = 0, ry = 0;
      var exponentX = this._picture.VdcExponent;
      var exponentY = this._picture.VdcExponent;
      var allowed = this._picture.VdcExponentAllowed;
      while (true) {
        var b = source.Next();
        if (!_IsParameter(b)) {
          source.Unread(b);
          return;
        }

        // Minus nought announces the Huffman-coded incremental form.
        if ((b & 0x7F) == 0x50)
          throw new NotSupportedException($"The character-encoded element {element} uses Huffman-coded incremental points, which are not supported.");

        if (this._vdcInteger) {
          w.Int32(ix += this._Integer(element, b));
          w.Int32(iy += this._Integer(element));
        } else {
          w.Double(rx += this._Real(element, b, exponentX, allowed, out exponentX));
          w.Double(ry += this._Real(element, _Eof, exponentY, allowed, out exponentY));
        }

        if (flags)
          w.Enum(checked((int)this._Integer(element)));
      }
    }

    // ------------------------------------------------------------------------------------------
    // Colour lists
    // ------------------------------------------------------------------------------------------

    /// <summary>Reads bits six to a byte, most significant first; -1 once the parameters end.</summary>
    private sealed class BitReader(Source source, int first) {
      private int _byte = first;
      private int _bit = 5;

      internal long Read(int bits) {
        long value = 0;
        for (var i = 0; i < bits; ++i) {
          if (this._bit < 0) {
            this._byte = source.Next();
            if (!_IsParameter(this._byte)) {
              source.Unread(this._byte);
              return -1;
            }

            this._bit = 5;
          }

          value = value << 1 | (uint)((this._byte >> this._bit--) & 1);
        }

        return value;
      }
    }

    /// <summary>
    /// A colour list of <paramref name="count"/> entries in the stated mode and precision: indices,
    /// or red, green and blue triples. A list that stops short leaves the rest at nought.
    /// </summary>
    private long[] _ColourList(string element, long count, bool direct, int precision) {
      var per = direct ? 3 : 1;
      var values = new long[count * per];
      var n = 0L;
      var type = this._NextParameter(element) & 0x03;

      void Put(long index) {
        if (n < count)
          values[n] = index;
        ++n;
      }

      void PutDirect((int Red, int Green, int Blue) colour) {
        if (n < count) {
          values[n * 3] = colour.Red;
          values[n * 3 + 1] = colour.Green;
          values[n * 3 + 2] = colour.Blue;
        }

        ++n;
      }

      int NextOrStop() {
        var b = source.Next();
        if (_IsParameter(b))
          return b;
        source.Unread(b);
        return _Eof;
      }

      long Runs(long run) {
        if (run < 0)
          throw new InvalidDataException($"The character-encoded element {element} states a negative run.");
        return Math.Min(run, Math.Max(0, count - n));
      }

      switch (type, direct) {
        case (0, false):
          for (var b = NextOrStop(); b != _Eof && n < count; b = NextOrStop())
            Put(this._Integer(element, b));
          break;
        case (0, true):
          for (var b = NextOrStop(); b != _Eof && n < count; b = NextOrStop())
            PutDirect(this._Direct(element, b, precision));
          break;
        case (1, _): {
          var first = NextOrStop();
          if (first == _Eof)
            break;
          var bits = new BitReader(source, first);
          while (n < count) {
            var a = bits.Read(precision);
            if (a < 0)
              break;
            if (!direct) {
              Put(a);
              continue;
            }

            var g = bits.Read(precision);
            var bl = g < 0 ? -1 : bits.Read(precision);
            if (bl < 0)
              break;
            PutDirect(((int)a, (int)g, (int)bl));
          }

          break;
        }
        case (2, false):
          for (var b = NextOrStop(); b != _Eof && n < count; b = NextOrStop()) {
            var index = this._Integer(element, b);
            for (var run = Runs(this._Integer(element)); run > 0; --run)
              Put(index);
          }

          break;
        case (2, true) or (3, true):
          // The run-length bit stream of direct colours reads as plain runs, as RAL-CGM reads it.
          for (var b = NextOrStop(); b != _Eof && n < count; b = NextOrStop()) {
            var colour = this._Direct(element, b, precision);
            for (var run = Runs(this._Integer(element)); run > 0; --run)
              PutDirect(colour);
          }

          break;
        case (3, false):
          // Each run's value is a bit stream of its own, starting on a fresh byte.
          for (var b = NextOrStop(); b != _Eof && n < count; b = NextOrStop()) {
            var index = new BitReader(source, b).Read(precision);
            if (index < 0)
              break;
            for (var run = Runs(this._Integer(element)); run > 0; --run)
              Put(index);
          }

          break;
      }

      this._SkipParameters();
      return values;
    }

    // ------------------------------------------------------------------------------------------
    // Elements
    // ------------------------------------------------------------------------------------------

    private void _Element(int code) {
      var w = new CgmCommandWriter();
      switch (code) {
        // Delimiters.
        case 0x3020: // BEGIN METAFILE
          this._BeginMetafile(w);
          return;
        case 0x3021: // END METAFILE
          this._Emit(0, 2);
          source.ClearSubstitution();
          this._ended = true;
          return;
        case 0x3022: { // BEGIN PICTURE
          this._SkipParametersUpToString(out var title);
          w.String(title);
          this._Emit(0, 3, w);
          this._picture = this._defaults;
          this._PictureDefaults();
          return;
        }
        case 0x3023: // BEGIN PICTURE BODY
          this._Emit(0, 4);
          break;
        case 0x3024: // END PICTURE
          this._Emit(0, 5);
          break;
        case 0x312B: // BEGIN METAFILE DEFAULTS REPLACEMENT
          // The elements it holds follow as ordinary elements up to its end, and say how every
          // picture starts; the renderer reads them in place.
          break;
        case 0x312C: // END METAFILE DEFAULTS REPLACEMENT
          this._defaults = this._picture;
          break;

        // Metafile descriptor.
        case 0x3122: { // VDC TYPE
          this._vdcInteger = this._Integer("VDC TYPE") == 0;
          w.Enum(this._vdcInteger ? 0 : 1);
          this._Emit(1, 3, w);
          break;
        }
        case 0x3124: { // REAL PRECISION
          const string element = "REAL PRECISION";
          _ = this._Integer(element);
          _ = this._Integer(element);
          this._realExponent = checked((int)this._Integer(element));
          this._realExponentAllowed = this._Integer(element) == 0;
          break;
        }
        case 0x3126: { // COLOUR PRECISION
          const string element = "COLOUR PRECISION";
          var precision = this._Integer(element);
          if (precision is < 1 or > 16)
            throw new InvalidDataException($"A character-encoded metafile states a colour precision of {precision} bits.");
          this._colourPrecision = (int)precision;
          this._ColourPrecision();
          if (!this._extentStated) {
            var maximum = (1L << this._colourPrecision) - 1;
            this._ColourExtent((0, 0, 0), ((int)maximum, (int)maximum, (int)maximum));
          }

          break;
        }
        case 0x3127: // COLOUR INDEX PRECISION
          this._indexPrecision = checked((int)this._Integer("COLOUR INDEX PRECISION"));
          break;
        case 0x3129: { // COLOUR VALUE EXTENT
          const string element = "COLOUR VALUE EXTENT";
          var minimum = this._Direct(element, _Eof, this._colourPrecision);
          var maximum = this._Direct(element, _Eof, this._colourPrecision);
          this._extentStated = true;
          this._ColourExtent(minimum, maximum);
          break;
        }

        // Picture descriptor.
        case 0x3221: { // COLOUR SELECTION MODE
          this._picture.Direct = this._Integer("COLOUR SELECTION MODE") == 1;
          w.Enum(this._picture.Direct ? 1 : 0);
          this._Emit(2, 2, w);
          break;
        }
        case 0x3222: { // LINE WIDTH SPECIFICATION MODE
          var mode = checked((int)this._Integer("LINE WIDTH SPECIFICATION MODE"));
          this._picture.LineWidthAbsolute = mode == 0;
          w.Enum(mode);
          this._Emit(2, 3, w);
          break;
        }
        case 0x3224: { // EDGE WIDTH SPECIFICATION MODE
          var mode = checked((int)this._Integer("EDGE WIDTH SPECIFICATION MODE"));
          this._picture.EdgeWidthAbsolute = mode == 0;
          w.Enum(mode);
          this._Emit(2, 5, w);
          break;
        }
        case 0x3225: // VDC EXTENT
          for (var i = 0; i < 4; ++i)
            this._Vdc("VDC EXTENT", w);
          this._Emit(2, 6, w);
          break;
        case 0x3226: // BACKGROUND COLOUR
          this._WriteDirect(w, this._Direct("BACKGROUND COLOUR", _Eof, this._colourPrecision));
          this._Emit(2, 7, w);
          break;

        // Control.
        case 0x3321: { // VDC REAL PRECISION
          const string element = "VDC REAL PRECISION";
          _ = this._Integer(element);
          _ = this._Integer(element);
          this._picture.VdcExponent = checked((int)this._Integer(element));
          this._picture.VdcExponentAllowed = this._Integer(element) == 0;
          break;
        }

        // Primitives.
        case 0x20: // POLYLINE
          this._Points("POLYLINE", w, false);
          this._Emit(4, 1, w);
          break;
        case 0x21: // DISJOINT POLYLINE
          this._Points("DISJOINT POLYLINE", w, false);
          this._Emit(4, 2, w);
          break;
        case 0x26: // POLYGON
          this._Points("POLYGON", w, false);
          this._Emit(4, 7, w);
          break;
        case 0x27: // POLYGON SET
          this._Points("POLYGON SET", w, true);
          this._Emit(4, 8, w);
          break;
        case 0x28: // CELL ARRAY
          this._CellArray(w);
          break;
        case 0x2A: // RECTANGLE
          for (var i = 0; i < 4; ++i)
            this._Vdc("RECTANGLE", w);
          this._Emit(4, 11, w);
          break;
        case 0x3420: // CIRCLE
          for (var i = 0; i < 3; ++i)
            this._Vdc("CIRCLE", w);
          this._Emit(4, 12, w);
          break;
        case 0x3423: // CIRCULAR ARC CENTRE
          for (var i = 0; i < 7; ++i)
            this._Vdc("CIRCULAR ARC CENTRE", w);
          this._Emit(4, 15, w);
          break;
        case 0x3424: // CIRCULAR ARC CENTRE CLOSE
          for (var i = 0; i < 7; ++i)
            this._Vdc("CIRCULAR ARC CENTRE CLOSE", w);
          w.Enum(checked((int)this._Integer("CIRCULAR ARC CENTRE CLOSE")));
          this._Emit(4, 16, w);
          break;
        case 0x3425: // ELLIPSE
          for (var i = 0; i < 6; ++i)
            this._Vdc("ELLIPSE", w);
          this._Emit(4, 17, w);
          break;

        // Attributes.
        case 0x3521: // LINE TYPE
          w.Int32(this._Integer("LINE TYPE"));
          this._Emit(5, 2, w);
          break;
        case 0x3522: // LINE WIDTH
          this._Width("LINE WIDTH", w, this._picture.LineWidthAbsolute);
          this._Emit(5, 3, w);
          break;
        case 0x3523: // LINE COLOUR
          this._Colour("LINE COLOUR", w);
          this._Emit(5, 4, w);
          break;
        case 0x3621: // INTERIOR STYLE
          w.Enum(checked((int)this._Integer("INTERIOR STYLE")));
          this._Emit(5, 22, w);
          break;
        case 0x3622: // FILL COLOUR
          this._Colour("FILL COLOUR", w);
          this._Emit(5, 23, w);
          break;
        case 0x3623: // HATCH INDEX
          w.Int32(this._Integer("HATCH INDEX"));
          this._Emit(5, 24, w);
          break;
        case 0x3626: // EDGE TYPE
          w.Int32(this._Integer("EDGE TYPE"));
          this._Emit(5, 27, w);
          break;
        case 0x3627: // EDGE WIDTH
          this._Width("EDGE WIDTH", w, this._picture.EdgeWidthAbsolute);
          this._Emit(5, 28, w);
          break;
        case 0x3628: // EDGE COLOUR
          this._Colour("EDGE COLOUR", w);
          this._Emit(5, 29, w);
          break;
        case 0x3629: // EDGE VISIBILITY
          w.Enum(checked((int)this._Integer("EDGE VISIBILITY")));
          this._Emit(5, 30, w);
          break;
        case 0x3630: { // COLOUR TABLE
          const string element = "COLOUR TABLE";
          var start = this._Integer(element);
          // The entries run on to the next element, so the list is read until it stops.
          var entries = this._OpenColourList(element, this._colourPrecision);
          w.UInt(start, 32);
          for (var i = 0; i + 2 < entries.Count; i += 3)
            this._WriteDirect(w, ((int)entries[i], (int)entries[i + 1], (int)entries[i + 2]));
          this._Emit(5, 34, w);
          return;
        }

        default:
          // Not drawn here and not changing how anything drawn is read.
          break;
      }

      this._SkipParameters();
    }

    private void _BeginMetafile(CgmCommandWriter w) {
      source.ClearSubstitution();

      // The characters to substitute, each sent as the character 0x40 above or below it, up to
      // ESC \ — and the substitution starts applying while the list is still being read.
      var c = source.Next();
      while (c != _Eof) {
        if (c == 0x1B || (source.Substituted[0x1B] && c == 0x5B)) {
          c = source.Next();
          if (c == 0x5C)
            break;
          continue;
        }

        if (c == 0x7E) {
          c = source.Next();
          continue;
        }

        var named = c < 0x40 ? c + 0x40 : c - 0x40;
        if (named <= 0x20 || named == 0x7F) {
          source.Substituted[named == 0x7F ? 0x21 : named] = true;
          source.Substituting = true;
        }

        c = source.Next();
      }

      w.String(this._String());
      this._Emit(0, 1, w);
      CgmTranslation.Precisions(this.Commands);

      // The character encoding's own defaults: six bits a colour component, nought to 63.
      this._ColourPrecision();
      this._ColourExtent((0, 0, 0), (63, 63, 63));
      this._SkipParameters();
    }

    /// <summary>The parameters before a string, then the string, which is all BEGIN PICTURE has.</summary>
    private void _SkipParametersUpToString(out string text) {
      text = string.Empty;
      while (true) {
        var c = source.Next();
        if (c == 0x1B) {
          var d = source.Next();
          if (d == 0x58) {
            text = this._String();
            break;
          }

          source.Unread(d);
          source.Unread(c);
          return;
        }

        if (c == 0x98) {
          text = this._String();
          break;
        }

        if (!_IsParameter(c)) {
          source.Unread(c);
          return;
        }
      }

      this._SkipParameters();
    }

    private void _ColourPrecision() {
      this._colourBits = CgmTranslation.BitsFor((1L << this._colourPrecision) - 1);
      var w = new CgmCommandWriter();
      w.Int32(this._colourBits);
      this._Emit(1, 7, w);
    }

    private void _ColourExtent((int Red, int Green, int Blue) minimum, (int Red, int Green, int Blue) maximum) {
      var w = new CgmCommandWriter();
      this._WriteDirect(w, minimum);
      this._WriteDirect(w, maximum);
      this._Emit(1, 10, w);
    }

    /// <summary>How every picture starts, stated after its BEGIN PICTURE so the renderer starts there too.</summary>
    private void _PictureDefaults() {
      void Mode(int id, int value) {
        var w = new CgmCommandWriter();
        w.Enum(value);
        this._Emit(2, id, w);
      }

      Mode(2, this._picture.Direct ? 1 : 0);
      Mode(3, this._picture.LineWidthAbsolute ? 0 : 1);
      Mode(5, this._picture.EdgeWidthAbsolute ? 0 : 1);
    }

    private void _Width(string element, CgmCommandWriter w, bool absolute) {
      if (absolute)
        this._Vdc(element, w);
      else
        w.Double(this._Real(element));
    }

    /// <summary>A direct colour list of unstated length: a colour table's entries.</summary>
    private List<long> _OpenColourList(string element, int precision) {
      var entries = new List<long>();
      var type = this._NextParameter(element) & 0x03;
      int NextOrStop() {
        var b = source.Next();
        if (_IsParameter(b))
          return b;
        source.Unread(b);
        return _Eof;
      }

      void Add((int Red, int Green, int Blue) colour) {
        if (entries.Count >= 3 * 65536)
          throw new InvalidDataException($"The character-encoded element {element} lists more colours than a table holds.");
        entries.Add(colour.Red);
        entries.Add(colour.Green);
        entries.Add(colour.Blue);
      }

      if (type == 1) {
        var first = NextOrStop();
        if (first != _Eof) {
          var bits = new BitReader(source, first);
          while (true) {
            var r = bits.Read(precision);
            var g = r < 0 ? -1 : bits.Read(precision);
            var b = g < 0 ? -1 : bits.Read(precision);
            if (b < 0)
              break;
            Add(((int)r, (int)g, (int)b));
          }
        }
      } else {
        for (var b = NextOrStop(); b != _Eof; b = NextOrStop()) {
          var colour = this._Direct(element, b, precision);
          var run = type == 0 ? 1 : this._Integer(element);
          if (run is < 0 or > 65536)
            throw new InvalidDataException($"The character-encoded element {element} states a run of {run}.");
          for (; run > 0; --run)
            Add(colour);
        }
      }

      this._SkipParameters();
      return entries;
    }

    private void _CellArray(CgmCommandWriter w) {
      const string element = "CELL ARRAY";
      for (var i = 0; i < 6; ++i)
        this._Vdc(element, w);
      var nx = this._Integer(element);
      var ny = this._Integer(element);
      var stated = checked((int)this._Integer(element));
      if (nx <= 0 || ny <= 0 || nx * ny > _MaximumCells)
        throw new InvalidDataException($"A character-encoded cell array of {nx} by {ny} cells cannot be drawn.");

      var direct = this._picture.Direct;
      var precision = stated != 0 ? stated : direct ? this._colourPrecision : this._indexPrecision;
      if (precision < 1 || precision > (direct ? 16 : 32))
        throw new InvalidDataException($"A character-encoded cell array states a precision of {precision} bits.");

      var values = this._ColourList(element, nx * ny, direct, precision);

      w.Int32(nx);
      w.Int32(ny);
      var bits = direct ? Math.Max(this._colourBits, CgmTranslation.BitsFor((1L << precision) - 1)) : 32;
      w.Int32(bits);
      w.Enum(1); // packed
      var per = direct ? 3 : 1;
      for (var row = 0L; row < ny; ++row) {
        for (var column = 0L; column < nx; ++column)
          for (var k = 0; k < per; ++k)
            w.UInt(values[(row * nx + column) * per + k], bits);
        w.Align();
      }

      this._Emit(4, 9, w);
    }
  }
}
