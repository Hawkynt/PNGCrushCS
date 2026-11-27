using System;
using System.IO;

namespace FileFormat.Pdf.Fonts;

/// <summary>Runs a Type 2 charstring into a glyph outline.</summary>
/// <remarks>
/// Converted from <c>compileCharString</c> in pdf.js's <c>font_renderer.js</c>, with the operator
/// semantics of the Type 2 Charstring Format (Adobe Technical Note #5177). Hints are counted, not
/// applied — the count is needed to know how many mask bytes a hintmask carries — and the optional
/// width that precedes the first stack-clearing operator is read off, which pdf.js discards and the
/// renderer here wants as the glyph's advance.
/// </remarks>
internal sealed class CffCharStringInterpreter {

  /// <summary>How deep subroutine calls may nest; the limit OTS and pdf.js use.</summary>
  private const int _MaxNesting = 10;

  /// <summary>How many operators and operands one glyph may run, subroutines included.</summary>
  private const int _MaxTokens = 1 << 20;

  private readonly byte[] _font;
  private readonly CffIndex _globalSubrs;
  private readonly CffIndex _localSubrs;
  private readonly int _globalBias;
  private readonly int _localBias;
  private readonly double _defaultWidth;
  private readonly double _nominalWidth;
  private readonly GlyphOutline _outline;
  private readonly double _offsetX, _offsetY;
  private readonly double[] _stack = new double[96];
  private readonly double[] _transient = new double[32];
  private int _count;
  private double _x, _y;
  private int _stems;
  private bool _widthRead;
  private bool _ended;
  private int _tokens;
  private uint _random = 0x2545F491;

  public CffCharStringInterpreter(byte[] font, CffIndex globalSubrs, CffIndex localSubrs, double defaultWidth, double nominalWidth, GlyphOutline outline, double offsetX, double offsetY) {
    this._font = font;
    this._globalSubrs = globalSubrs;
    this._localSubrs = localSubrs;
    this._globalBias = SubroutineBias(globalSubrs.Count);
    this._localBias = SubroutineBias(localSubrs.Count);
    this._defaultWidth = defaultWidth;
    this._nominalWidth = nominalWidth;
    this._outline = outline;
    this._offsetX = offsetX;
    this._offsetY = offsetY;
    this.Width = defaultWidth;
  }

  /// <summary>The glyph's advance in font units.</summary>
  public double Width { get; private set; }

  /// <summary>An accented character's parts, when the glyph ends with the four-argument endchar.</summary>
  public (double Adx, double Ady, int BaseCode, int AccentCode)? Seac { get; private set; }

  /// <summary>The number added to a subroutine operand to index the INDEX, which depends on its size.</summary>
  public static int SubroutineBias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

  /// <summary>Runs the charstring.</summary>
  public void Run(ReadOnlySpan<byte> code) => this._Parse(code, 0);

  private void _Parse(ReadOnlySpan<byte> code, int depth) {
    if (depth > _MaxNesting)
      throw new InvalidDataException("Type 2 charstring subroutines nest too deeply.");

    var i = 0;
    while (i < code.Length && !this._ended) {
      if (++this._tokens > _MaxTokens)
        throw new InvalidDataException("Type 2 charstring runs too long.");

      int v = code[i++];
      if (v >= 32 || v == 28) {
        double value;
        if (v == 28) {
          if (i + 2 > code.Length)
            throw new InvalidDataException("Truncated Type 2 number.");
          value = (short)((code[i] << 8) | code[i + 1]);
          i += 2;
        } else if (v <= 246)
          value = v - 139;
        else if (v <= 250) {
          value = (v - 247) * 256 + code[i++] + 108;
        } else if (v <= 254) {
          value = -(v - 251) * 256 - code[i++] - 108;
        } else {
          if (i + 4 > code.Length)
            throw new InvalidDataException("Truncated Type 2 number.");
          value = ((code[i] << 24) | (code[i + 1] << 16) | (code[i + 2] << 8) | code[i + 3]) / 65536.0;
          i += 4;
        }

        this._Push(value);
        continue;
      }

      switch (v) {
        case 1: // hstem
        case 3: // vstem
        case 18: // hstemhm
        case 23: // vstemhm
          this._ReadWidth(this._count % 2 == 1);
          this._stems += this._count >> 1;
          this._count = 0;
          break;

        case 19: // hintmask
        case 20: // cntrmask
          this._ReadWidth(this._count % 2 == 1);
          this._stems += this._count >> 1;
          i += (this._stems + 7) >> 3;
          this._count = 0;
          break;

        case 21: // rmoveto
          this._ReadWidth(this._count > 2);
          this._Need(2);
          this._x += this._stack[0];
          this._y += this._stack[1];
          this._MoveTo();
          this._count = 0;
          break;

        case 22: // hmoveto
          this._ReadWidth(this._count > 1);
          this._Need(1);
          this._x += this._stack[0];
          this._MoveTo();
          this._count = 0;
          break;

        case 4: // vmoveto
          this._ReadWidth(this._count > 1);
          this._Need(1);
          this._y += this._stack[0];
          this._MoveTo();
          this._count = 0;
          break;

        case 5: { // rlineto
          var k = 0;
          while (k + 2 <= this._count) {
            this._x += this._stack[k++];
            this._y += this._stack[k++];
            this._LineTo();
          }
          this._count = 0;
          break;
        }

        case 6: // hlineto
        case 7: { // vlineto
          var horizontal = v == 6;
          for (var k = 0; k < this._count; ++k) {
            if (horizontal)
              this._x += this._stack[k];
            else
              this._y += this._stack[k];
            this._LineTo();
            horizontal = !horizontal;
          }
          this._count = 0;
          break;
        }

        case 8: { // rrcurveto
          var k = 0;
          while (k + 6 <= this._count) {
            this._RelativeCurve(k);
            k += 6;
          }
          this._count = 0;
          break;
        }

        case 24: { // rcurveline
          var k = 0;
          while (this._count - k > 2 && k + 6 <= this._count) {
            this._RelativeCurve(k);
            k += 6;
          }
          if (k + 2 <= this._count) {
            this._x += this._stack[k];
            this._y += this._stack[k + 1];
            this._LineTo();
          }
          this._count = 0;
          break;
        }

        case 25: { // rlinecurve
          var k = 0;
          while (this._count - k > 6) {
            this._x += this._stack[k++];
            this._y += this._stack[k++];
            this._LineTo();
          }
          if (k + 6 <= this._count)
            this._RelativeCurve(k);
          this._count = 0;
          break;
        }

        case 26: { // vvcurveto
          var k = 0;
          if (this._count % 2 == 1)
            this._x += this._stack[k++];
          while (k + 4 <= this._count) {
            var xa = this._x;
            var ya = this._y + this._stack[k];
            var xb = xa + this._stack[k + 1];
            var yb = ya + this._stack[k + 2];
            this._x = xb;
            this._y = yb + this._stack[k + 3];
            this._CurveTo(xa, ya, xb, yb);
            k += 4;
          }
          this._count = 0;
          break;
        }

        case 27: { // hhcurveto
          var k = 0;
          if (this._count % 2 == 1)
            this._y += this._stack[k++];
          while (k + 4 <= this._count) {
            var xa = this._x + this._stack[k];
            var ya = this._y;
            var xb = xa + this._stack[k + 1];
            var yb = ya + this._stack[k + 2];
            this._x = xb + this._stack[k + 3];
            this._y = yb;
            this._CurveTo(xa, ya, xb, yb);
            k += 4;
          }
          this._count = 0;
          break;
        }

        case 30: // vhcurveto
        case 31: { // hvcurveto
          var horizontal = v == 31;
          var k = 0;
          while (k + 4 <= this._count) {
            var last = this._count - k == 5;
            if (horizontal) {
              var xa = this._x + this._stack[k];
              var ya = this._y;
              var xb = xa + this._stack[k + 1];
              var yb = ya + this._stack[k + 2];
              this._y = yb + this._stack[k + 3];
              this._x = xb + (last ? this._stack[k + 4] : 0);
              this._CurveTo(xa, ya, xb, yb);
            } else {
              var xa = this._x;
              var ya = this._y + this._stack[k];
              var xb = xa + this._stack[k + 1];
              var yb = ya + this._stack[k + 2];
              this._x = xb + this._stack[k + 3];
              this._y = yb + (last ? this._stack[k + 4] : 0);
              this._CurveTo(xa, ya, xb, yb);
            }

            k += last ? 5 : 4;
            horizontal = !horizontal;
          }
          this._count = 0;
          break;
        }

        case 10: { // callsubr
          this._Need(1);
          var n = (int)this._stack[--this._count] + this._localBias;
          if ((uint)n < (uint)this._localSubrs.Count)
            this._Parse(this._localSubrs.Get(this._font, n), depth + 1);
          break;
        }

        case 29: { // callgsubr
          this._Need(1);
          var n = (int)this._stack[--this._count] + this._globalBias;
          if ((uint)n < (uint)this._globalSubrs.Count)
            this._Parse(this._globalSubrs.Get(this._font, n), depth + 1);
          break;
        }

        case 11: // return
          return;

        case 14: // endchar
          this._ReadWidth(this._count is 1 or 5);
          if (this._count >= 4)
            this.Seac = (this._stack[0], this._stack[1], (int)this._stack[2], (int)this._stack[3]);
          this._outline.Close();
          this._ended = true;
          return;

        case 12:
          if (i >= code.Length)
            throw new InvalidDataException("Truncated Type 2 escape operator.");
          this._Escape(code[i++]);
          break;

        default:
          throw new InvalidDataException($"Unknown Type 2 charstring operator {v}.");
      }
    }
  }

  private void _Escape(int op) {
    var s = this._stack;
    switch (op) {
      case 34: { // hflex
        this._Need(7);
        var y = this._y;
        var xa = this._x + s[0];
        var xb = xa + s[1];
        var y1 = y + s[2];
        this._x = xb + s[3];
        this._y = y1;
        this._CurveTo(xa, y, xb, y1);
        xa = this._x + s[4];
        xb = xa + s[5];
        this._x = xb + s[6];
        this._y = y;
        this._CurveTo(xa, y1, xb, y);
        this._count = 0;
        break;
      }

      case 35: // flex
        this._Need(13);
        this._RelativeCurve(0);
        this._RelativeCurve(6);
        this._count = 0;
        break;

      case 36: { // hflex1
        this._Need(9);
        var y = this._y;
        var xa = this._x + s[0];
        var y1 = y + s[1];
        var xb = xa + s[2];
        var y2 = y1 + s[3];
        this._x = xb + s[4];
        this._y = y2;
        this._CurveTo(xa, y1, xb, y2);
        xa = this._x + s[5];
        xb = xa + s[6];
        var y3 = y2 + s[7];
        this._x = xb + s[8];
        this._y = y;
        this._CurveTo(xa, y2, xb, y3);
        this._count = 0;
        break;
      }

      case 37: { // flex1
        this._Need(11);
        var x0 = this._x;
        var y0 = this._y;
        this._RelativeCurve(0);
        var xa = this._x + s[6];
        var ya = this._y + s[7];
        var xb = xa + s[8];
        var yb = ya + s[9];
        if (Math.Abs(xb - x0) > Math.Abs(yb - y0)) {
          this._x = xb + s[10];
          this._y = y0;
        } else {
          this._x = x0;
          this._y = yb + s[10];
        }
        this._CurveTo(xa, ya, xb, yb);
        this._count = 0;
        break;
      }

      case 0: // dotsection, deprecated and meaningless here
        this._count = 0;
        break;

      case 3: // and
        this._Binary(static (a, b) => a != 0 && b != 0 ? 1 : 0);
        break;
      case 4: // or
        this._Binary(static (a, b) => a != 0 || b != 0 ? 1 : 0);
        break;
      case 5: // not
        this._Need(1);
        s[this._count - 1] = s[this._count - 1] == 0 ? 1 : 0;
        break;
      case 9: // abs
        this._Need(1);
        s[this._count - 1] = Math.Abs(s[this._count - 1]);
        break;
      case 10: // add
        this._Binary(static (a, b) => a + b);
        break;
      case 11: // sub
        this._Binary(static (a, b) => a - b);
        break;
      case 12: // div
        this._Binary(static (a, b) => b == 0 ? 0 : a / b);
        break;
      case 14: // neg
        this._Need(1);
        s[this._count - 1] = -s[this._count - 1];
        break;
      case 15: // eq
        this._Binary(static (a, b) => a == b ? 1 : 0);
        break;
      case 18: // drop
        this._Need(1);
        --this._count;
        break;
      case 20: { // put
        this._Need(2);
        var index = (int)s[this._count - 1];
        if ((uint)index < (uint)this._transient.Length)
          this._transient[index] = s[this._count - 2];
        this._count -= 2;
        break;
      }
      case 21: { // get
        this._Need(1);
        var index = (int)s[this._count - 1];
        s[this._count - 1] = (uint)index < (uint)this._transient.Length ? this._transient[index] : 0;
        break;
      }
      case 22: { // ifelse
        this._Need(4);
        var v2 = s[this._count - 1];
        var v1 = s[this._count - 2];
        var s2 = s[this._count - 3];
        var s1 = s[this._count - 4];
        this._count -= 4;
        this._Push(v1 <= v2 ? s1 : s2);
        break;
      }
      case 23: // random: any number in (0, 1]; a fixed generator keeps renders repeatable
        this._random ^= this._random << 13;
        this._random ^= this._random >> 17;
        this._random ^= this._random << 5;
        this._Push((this._random % 65535 + 1) / 65536.0);
        break;
      case 24: // mul
        this._Binary(static (a, b) => a * b);
        break;
      case 26: // sqrt
        this._Need(1);
        s[this._count - 1] = Math.Sqrt(Math.Abs(s[this._count - 1]));
        break;
      case 27: // dup
        this._Need(1);
        this._Push(s[this._count - 1]);
        break;
      case 28: { // exch
        this._Need(2);
        (s[this._count - 1], s[this._count - 2]) = (s[this._count - 2], s[this._count - 1]);
        break;
      }
      case 29: { // index
        this._Need(1);
        var index = (int)s[this._count - 1];
        if (index < 0)
          index = 0;
        var from = this._count - 2 - index;
        s[this._count - 1] = from >= 0 ? s[from] : 0;
        break;
      }
      case 30: { // roll
        this._Need(2);
        var shift = (int)s[this._count - 1];
        var n = (int)s[this._count - 2];
        this._count -= 2;
        if (n > 0 && n <= this._count) {
          var start = this._count - n;
          var temp = new double[n];
          for (var k = 0; k < n; ++k)
            temp[(((k + shift) % n) + n) % n] = s[start + k];
          Array.Copy(temp, 0, s, start, n);
        }
        break;
      }

      default:
        throw new InvalidDataException($"Unknown Type 2 charstring operator 12 {op}.");
    }
  }

  private void _Binary(Func<double, double, double> operation) {
    this._Need(2);
    var b = this._stack[--this._count];
    var a = this._stack[this._count - 1];
    this._stack[this._count - 1] = operation(a, b);
  }

  // The first stack-clearing operator may carry the width as one extra operand at the bottom.
  private void _ReadWidth(bool present) {
    if (this._widthRead)
      return;

    this._widthRead = true;
    if (!present || this._count == 0)
      return;

    this.Width = this._nominalWidth + this._stack[0];
    Array.Copy(this._stack, 1, this._stack, 0, this._count - 1);
    --this._count;
  }

  private void _Push(double value) {
    if (this._count >= this._stack.Length)
      throw new InvalidDataException("Type 2 charstring overflows its stack.");

    this._stack[this._count++] = value;
  }

  private void _Need(int count) {
    if (this._count < count)
      throw new InvalidDataException("Type 2 charstring underflows its stack.");
  }

  private void _RelativeCurve(int k) {
    var s = this._stack;
    var xa = this._x + s[k];
    var ya = this._y + s[k + 1];
    var xb = xa + s[k + 2];
    var yb = ya + s[k + 3];
    this._x = xb + s[k + 4];
    this._y = yb + s[k + 5];
    this._CurveTo(xa, ya, xb, yb);
  }

  private void _MoveTo() {
    this._outline.Close();
    this._outline.MoveTo(this._x + this._offsetX, this._y + this._offsetY);
  }

  private void _LineTo() => this._outline.LineTo(this._x + this._offsetX, this._y + this._offsetY);

  private void _CurveTo(double xa, double ya, double xb, double yb) {
    var ox = this._offsetX;
    var oy = this._offsetY;
    this._outline.CurveTo(xa + ox, ya + oy, xb + ox, yb + oy, this._x + ox, this._y + oy);
  }
}
