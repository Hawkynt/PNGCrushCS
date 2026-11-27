using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pdf.Fonts;

/// <summary>Runs a decrypted Type 1 charstring into a glyph outline.</summary>
/// <remarks>
/// The operator semantics follow the Adobe Type 1 Font Format (chapter 6 and the OtherSubrs of
/// chapter 8) and the way pdf.js's <c>Type1CharString</c> in <c>type1_parser.js</c> treats real
/// fonts: hints are dropped, closepath closes, flex is gathered from the seven moves between
/// OtherSubrs 1 and 0 and drawn as its two curves, hint replacement (OtherSubr 3) hands its
/// subroutine number back through <c>pop</c>. pdf.js converts to Type 2 and draws that; drawing
/// directly gives the same outline without the round trip.
/// </remarks>
internal sealed class Type1CharStringInterpreter {

  /// <summary>Subroutine nesting limit, the one FreeType uses.</summary>
  private const int _MaxNesting = 16;

  private const int _MaxTokens = 1 << 20;

  private readonly IReadOnlyList<byte[]?> _subrs;
  private readonly GlyphOutline _outline;
  private readonly double _offsetX, _offsetY;
  private readonly double[] _stack = new double[64];
  private readonly Stack<double> _psStack = new();
  private readonly List<(double X, double Y)> _flexPoints = [];
  private int _count;
  private double _x, _y;
  private bool _flexing;
  private bool _ended;
  private int _tokens;

  public Type1CharStringInterpreter(IReadOnlyList<byte[]?> subrs, GlyphOutline outline, double offsetX, double offsetY) {
    this._subrs = subrs;
    this._outline = outline;
    this._offsetX = offsetX;
    this._offsetY = offsetY;
  }

  /// <summary>The advance width set by hsbw or sbw, in font units.</summary>
  public double Width { get; private set; }

  /// <summary>The left sidebearing set by hsbw or sbw.</summary>
  public double SideBearingX { get; private set; }

  /// <summary>An accented character's parts: where the accent goes relative to the base, and both codes in StandardEncoding.</summary>
  public (double Adx, double Ady, int BaseCode, int AccentCode)? Seac { get; private set; }

  /// <summary>Runs the charstring.</summary>
  public void Run(byte[] code) => this._Parse(code, 0);

  private void _Parse(byte[] code, int depth) {
    if (depth > _MaxNesting)
      throw new InvalidDataException("Type 1 charstring subroutines nest too deeply.");

    var i = 0;
    while (i < code.Length && !this._ended) {
      if (++this._tokens > _MaxTokens)
        throw new InvalidDataException("Type 1 charstring runs too long.");

      int v = code[i++];
      if (v >= 32) {
        double value;
        if (v <= 246)
          value = v - 139;
        else if (v <= 250)
          value = (v - 247) * 256 + _At(code, i++) + 108;
        else if (v <= 254)
          value = -(v - 251) * 256 - _At(code, i++) - 108;
        else {
          value = (_At(code, i) << 24) | (_At(code, i + 1) << 16) | (_At(code, i + 2) << 8) | _At(code, i + 3);
          i += 4;
        }

        this._Push(value);
        continue;
      }

      var s = this._stack;
      switch (v) {
        case 1: // hstem
        case 3: // vstem
          this._count = 0;
          break;

        case 4: // vmoveto
          this._Need(1);
          this._Move(0, s[this._count - 1]);
          this._count = 0;
          break;

        case 5: // rlineto
          this._Need(2);
          this._x += s[this._count - 2];
          this._y += s[this._count - 1];
          this._LineTo();
          this._count = 0;
          break;

        case 6: // hlineto
          this._Need(1);
          this._x += s[this._count - 1];
          this._LineTo();
          this._count = 0;
          break;

        case 7: // vlineto
          this._Need(1);
          this._y += s[this._count - 1];
          this._LineTo();
          this._count = 0;
          break;

        case 8: { // rrcurveto
          this._Need(6);
          var k = this._count - 6;
          var xa = this._x + s[k];
          var ya = this._y + s[k + 1];
          var xb = xa + s[k + 2];
          var yb = ya + s[k + 3];
          this._x = xb + s[k + 4];
          this._y = yb + s[k + 5];
          this._CurveTo(xa, ya, xb, yb);
          this._count = 0;
          break;
        }

        case 9: // closepath
          this._outline.Close();
          this._count = 0;
          break;

        case 10: { // callsubr
          this._Need(1);
          var n = (int)s[--this._count];
          var subr = (uint)n < (uint)this._subrs.Count ? this._subrs[n] : null;
          if (subr == null)
            throw new InvalidDataException($"Type 1 charstring calls missing subroutine {n}.");
          this._Parse(subr, depth + 1);
          break;
        }

        case 11: // return
          return;

        case 13: // hsbw
          this._Need(2);
          this.SideBearingX = s[this._count - 2];
          this.Width = s[this._count - 1];
          this._x = this.SideBearingX;
          this._y = 0;
          this._count = 0;
          break;

        case 14: // endchar
          this._outline.Close();
          this._ended = true;
          return;

        case 21: // rmoveto
          this._Need(2);
          this._Move(s[this._count - 2], s[this._count - 1]);
          this._count = 0;
          break;

        case 22: // hmoveto
          this._Need(1);
          this._Move(s[this._count - 1], 0);
          this._count = 0;
          break;

        case 30: { // vhcurveto
          this._Need(4);
          var k = this._count - 4;
          var xa = this._x;
          var ya = this._y + s[k];
          var xb = xa + s[k + 1];
          var yb = ya + s[k + 2];
          this._x = xb + s[k + 3];
          this._y = yb;
          this._CurveTo(xa, ya, xb, yb);
          this._count = 0;
          break;
        }

        case 31: { // hvcurveto
          this._Need(4);
          var k = this._count - 4;
          var xa = this._x + s[k];
          var ya = this._y;
          var xb = xa + s[k + 1];
          var yb = ya + s[k + 2];
          this._x = xb;
          this._y = yb + s[k + 3];
          this._CurveTo(xa, ya, xb, yb);
          this._count = 0;
          break;
        }

        case 12:
          if (i >= code.Length)
            throw new InvalidDataException("Truncated Type 1 escape operator.");
          this._Escape(code[i++]);
          break;

        default:
          // pdf.js warns and carries on over an unknown command, leaving the stack alone.
          break;
      }
    }
  }

  private void _Escape(int op) {
    var s = this._stack;
    switch (op) {
      case 0: // dotsection
      case 1: // vstem3
      case 2: // hstem3
        this._count = 0;
        break;

      case 6: { // seac
        this._Need(5);
        var k = this._count - 5;
        var asb = s[k];
        this.Seac = (s[k + 1] + this.SideBearingX - asb, s[k + 2], (int)s[k + 3], (int)s[k + 4]);
        this._outline.Close();
        this._ended = true;
        this._count = 0;
        break;
      }

      case 7: // sbw
        this._Need(4);
        this.SideBearingX = s[this._count - 4];
        this._x = this.SideBearingX;
        this._y = s[this._count - 3];
        this.Width = s[this._count - 2];
        this._count = 0;
        break;

      case 12: { // div
        this._Need(2);
        var b = s[--this._count];
        var a = s[this._count - 1];
        s[this._count - 1] = b == 0 ? 0 : a / b;
        break;
      }

      case 16: { // callothersubr
        this._Need(2);
        var number = (int)s[--this._count];
        var argCount = (int)s[--this._count];
        if (argCount < 0 || argCount > this._count)
          argCount = this._count;
        this._OtherSubr(number, argCount);
        break;
      }

      case 17: // pop
        this._Push(this._psStack.Count > 0 ? this._psStack.Pop() : 0);
        break;

      case 33: // setcurrentpoint
        if (this._count >= 2) {
          this._x = s[this._count - 2];
          this._y = s[this._count - 1];
        }
        this._count = 0;
        break;

      default:
        break;
    }
  }

  private void _OtherSubr(int number, int argCount) {
    var s = this._stack;
    var first = this._count - argCount;
    switch (number) {
      case 0 when this._flexing: {
        // Flex end: the reference point and six more, which are the two curves' points.
        this._flexing = false;
        var points = this._flexPoints;
        if (points.Count >= 7) {
          var (x1, y1) = points[1];
          var (x2, y2) = points[2];
          var (x3, y3) = points[3];
          this._x = x3;
          this._y = y3;
          this._CurveTo(x1, y1, x2, y2);
          (x1, y1) = points[4];
          (x2, y2) = points[5];
          (x3, y3) = points[6];
          this._x = x3;
          this._y = y3;
          this._CurveTo(x1, y1, x2, y2);
        }

        this._psStack.Clear();
        this._psStack.Push(this._y);
        this._psStack.Push(this._x);
        break;
      }

      case 1: // flex start
        this._flexing = true;
        this._flexPoints.Clear();
        this._psStack.Clear();
        break;

      case 2: // a flex point follows; the move itself records it
        this._psStack.Clear();
        break;

      default:
        // Hint replacement (3) and everything else hand their arguments back through pop, which
        // for 3 gives the subroutine the following callsubr runs.
        this._psStack.Clear();
        for (var k = first; k < this._count; ++k)
          this._psStack.Push(s[k]);
        break;
    }

    this._count = first;
  }

  private void _Move(double dx, double dy) {
    this._x += dx;
    this._y += dy;
    if (this._flexing) {
      this._flexPoints.Add((this._x, this._y));
      return;
    }

    this._outline.Close();
    this._outline.MoveTo(this._x + this._offsetX, this._y + this._offsetY);
  }

  private void _Push(double value) {
    if (this._count >= this._stack.Length)
      throw new InvalidDataException("Type 1 charstring overflows its stack.");

    this._stack[this._count++] = value;
  }

  private void _Need(int count) {
    if (this._count < count)
      throw new InvalidDataException("Type 1 charstring underflows its stack.");
  }

  private static int _At(byte[] code, int index) => index < code.Length ? code[index] : throw new InvalidDataException("Truncated Type 1 number.");

  private void _LineTo() => this._outline.LineTo(this._x + this._offsetX, this._y + this._offsetY);

  private void _CurveTo(double xa, double ya, double xb, double yb) {
    var ox = this._offsetX;
    var oy = this._offsetY;
    this._outline.CurveTo(xa + ox, ya + oy, xb + ox, yb + oy, this._x + ox, this._y + oy);
  }
}
