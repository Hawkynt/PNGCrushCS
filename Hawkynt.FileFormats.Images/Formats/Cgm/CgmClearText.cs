using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Cgm;

/// <summary>
/// Reads the clear-text encoding of a Computer Graphics Metafile (ISO/IEC 8632-4) by translating
/// it into the binary encoding's commands, which the renderer already draws.
/// </summary>
/// <remarks>
/// A clear-text metafile is a sequence of elements, each its name — <c>BEGMF</c>, <c>LINE</c>,
/// <c>COLRTABLE</c> and so on, in any case — then its parameters, then <c>;</c> or <c>/</c>. Numbers
/// may be integers, reals with an exponent, or based (<c>16#FF</c>); points may stand bare or in
/// parentheses, separated by spaces or commas; strings are quoted with either quote, the quote
/// doubled inside; and anything between two <c>%</c> is a comment.
/// <para/>
/// The translation writes its own precisions rather than the text's: 32-bit integers, indices and
/// integer coordinates, 64-bit floating reals and real coordinates, so every value the text can
/// state survives exactly. Only the colour precision follows the text, together with an explicit
/// colour value extent, because direct colour components mean a fraction of the extent the text
/// states. Elements the renderer does not use (fonts, text, markers, segments, escapes) are passed
/// over; every element that changes how later ones are read or drawn is translated.
/// <para/>
/// Written from the element names and parameter forms of the standard as RAL-CGM 3.50 reads and
/// writes them; RAL-CGM is under a non-commercial licence, so it is the oracle and not a source.
/// </remarks>
internal static class CgmClearText {

  /// <summary>Whether the data opens like a clear-text metafile: optional blanks and comments, then <c>BEGMF</c>.</summary>
  internal static bool Matches(ReadOnlySpan<byte> data) {
    var at = 0;
    while (at < data.Length) {
      var c = data[at];
      if (c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f') {
        ++at;
        continue;
      }

      if (c == '%') {
        var end = data[(at + 1)..].IndexOf((byte)'%');
        if (end < 0)
          return false;
        at += end + 2;
        continue;
      }

      break;
    }

    if (at + 5 > data.Length)
      return false;
    Span<byte> name = stackalloc byte[5];
    for (var i = 0; i < 5; ++i)
      name[i] = (byte)char.ToUpperInvariant((char)data[at + i]);
    return name.SequenceEqual("BEGMF"u8);
  }

  internal static List<CgmCommand> Translate(ReadOnlySpan<byte> data) {
    var tokens = _Tokenise(Encoding.Latin1.GetString(data));
    var translator = new Translator();
    var at = 0;
    while (at < tokens.Count) {
      if (tokens[at].Kind != TokenKind.Word)
        throw new InvalidDataException($"A clear-text metafile has '{tokens[at].Text}' where an element name should be.");
      var name = tokens[at].Text;
      var end = at + 1;
      while (end < tokens.Count && tokens[end].Kind != TokenKind.Terminator)
        ++end;
      translator.Element(name, tokens.GetRange(at + 1, end - at - 1));
      at = end + 1;
      if (translator.Ended)
        break;
    }

    if (!translator.Ended)
      throw new InvalidDataException("A clear-text metafile ends with ENDMF and this one does not.");
    return translator.Commands;
  }

  // ============================================================================================
  // Tokens
  // ============================================================================================

  private enum TokenKind { Word, Number, Text, Terminator }

  private readonly record struct Token(TokenKind Kind, string Text, double Value, bool IsInteger);

  private static List<Token> _Tokenise(string s) {
    var tokens = new List<Token>();
    var i = 0;
    while (i < s.Length) {
      var c = s[i];
      if (char.IsWhiteSpace(c) || c is ',' or '(' or ')') {
        ++i;
        continue;
      }

      if (c == '%') {
        var end = s.IndexOf('%', i + 1);
        i = end < 0 ? s.Length : end + 1;
        continue;
      }

      if (c is ';' or '/') {
        tokens.Add(new(TokenKind.Terminator, c.ToString(), 0, false));
        ++i;
        continue;
      }

      if (c is '\'' or '"') {
        var builder = new StringBuilder();
        ++i;
        while (i < s.Length) {
          if (s[i] == c) {
            if (i + 1 < s.Length && s[i + 1] == c) {
              builder.Append(c);
              i += 2;
              continue;
            }

            ++i;
            break;
          }

          builder.Append(s[i++]);
        }

        tokens.Add(new(TokenKind.Text, builder.ToString(), 0, false));
        continue;
      }

      if (char.IsDigit(c) || c is '+' or '-' or '.') {
        var start = i;
        ++i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '.' or '#' or '+' or '-') && !(s[i] is '+' or '-' && s[i - 1] is not ('E' or 'e')))
          ++i;
        tokens.Add(_Number(s[start..i]));
        continue;
      }

      if (char.IsLetter(c) || c is '_' or '$') {
        var start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$'))
          ++i;
        // Underscores and dollar signs inside names and keywords carry no meaning.
        tokens.Add(new(TokenKind.Word, s[start..i].Replace("_", string.Empty).Replace("$", string.Empty).ToUpperInvariant(), 0, false));
        continue;
      }

      throw new InvalidDataException($"A clear-text metafile holds a '{c}' no token begins with.");
    }

    return tokens;
  }

  private static Token _Number(string text) {
    var hash = text.IndexOf('#');
    if (hash > 0) {
      var negative = text.StartsWith('-');
      var radix = int.Parse(text.TrimStart('+', '-')[..(hash - (text[0] is '+' or '-' ? 1 : 0))], CultureInfo.InvariantCulture);
      long value = 0;
      foreach (var d in text[(hash + 1)..]) {
        var digit = char.IsDigit(d) ? d - '0' : char.ToUpperInvariant(d) - 'A' + 10;
        if (digit < 0 || digit >= radix)
          throw new InvalidDataException($"A clear-text metafile states '{text}', which is not a number in base {radix}.");
        value = value * radix + digit;
      }

      return new(TokenKind.Number, text, negative ? -value : value, true);
    }

    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
      throw new InvalidDataException($"A clear-text metafile states '{text}', which is not a number.");
    var integer = text.IndexOfAny(['.', 'e', 'E']) < 0;
    return new(TokenKind.Number, text, number, integer);
  }

  // ============================================================================================
  // Translation into binary commands
  // ============================================================================================

  private sealed class Translator {
    internal readonly List<CgmCommand> Commands = [];
    internal bool Ended;

    private bool _vdcInteger = true;
    private bool _direct;
    private int _colourBits = 8;
    private bool _lineWidthAbsolute;
    private bool _edgeWidthAbsolute;

    private void _Emit(int elementClass, int elementId, CgmCommandWriter? writer = null)
      => this.Commands.Add(new(elementClass, elementId, writer?.ToArray() ?? []));

    private sealed class Arguments(string element, List<Token> tokens) {
      private int _at;
      internal bool More => this._at < tokens.Count;
      internal int Count => tokens.Count - this._at;

      internal double Number() {
        if (this._at >= tokens.Count || tokens[this._at].Kind != TokenKind.Number)
          throw new InvalidDataException($"The clear-text element {element} lacks a number where one belongs.");
        return tokens[this._at++].Value;
      }

      internal long Integer() => (long)Math.Round(this.Number());

      internal string Word() {
        if (this._at >= tokens.Count || tokens[this._at].Kind != TokenKind.Word)
          throw new InvalidDataException($"The clear-text element {element} lacks a keyword where one belongs.");
        return tokens[this._at++].Text;
      }

      internal string Text() {
        if (this._at >= tokens.Count || tokens[this._at].Kind != TokenKind.Text)
          throw new InvalidDataException($"The clear-text element {element} lacks a string where one belongs.");
        return tokens[this._at++].Text;
      }

      internal bool NextIsWord => this._at < tokens.Count && tokens[this._at].Kind == TokenKind.Word;
    }

    /// <summary>Which of the choices a keyword names; a choice may list alternative spellings separated by <c>|</c>.</summary>
    private static int _Keyword(string element, string word, params string[] choices) {
      for (var i = 0; i < choices.Length; ++i)
        foreach (var spelling in choices[i].Split('|'))
          if (word == spelling)
            return i;
      throw new InvalidDataException($"The clear-text element {element} states '{word}', which is not one of {string.Join(", ", choices)}.");
    }

    private void _Vdc(CgmCommandWriter w, Arguments a) {
      if (this._vdcInteger)
        w.Int32(a.Integer());
      else
        w.Double(a.Number());
    }

    private void _Colour(CgmCommandWriter w, Arguments a) {
      if (this._direct)
        for (var i = 0; i < 3; ++i)
          w.UInt(a.Integer(), this._colourBits);
      else
        w.UInt(a.Integer(), 32);
    }

    private void _DirectColour(CgmCommandWriter w, Arguments a) {
      for (var i = 0; i < 3; ++i)
        w.UInt(a.Integer(), this._colourBits);
    }

    internal void Element(string name, List<Token> tokens) {
      var a = new Arguments(name, tokens);
      var w = new CgmCommandWriter();
      switch (name) {
        // Delimiters.
        case "BEGMF":
          w.String(a.More ? a.Text() : string.Empty);
          this._Emit(0, 1, w);
          CgmTranslation.Precisions(this.Commands);
          break;
        case "ENDMF":
          this._Emit(0, 2);
          this.Ended = true;
          break;
        case "BEGPIC":
          w.String(a.More ? a.Text() : string.Empty);
          this._Emit(0, 3, w);
          break;
        case "BEGPICBODY":
          this._Emit(0, 4);
          break;
        case "ENDPIC":
          this._Emit(0, 5);
          break;

        // Metafile descriptor: what changes how the rest is read.
        case "VDCTYPE":
          this._vdcInteger = _Keyword(name, a.Word(), "INTEGER", "REAL") == 0;
          w.Enum(this._vdcInteger ? 0 : 1);
          this._Emit(1, 3, w);
          break;
        case "COLRPREC": {
          var maximum = a.Integer();
          this._colourBits = CgmTranslation.BitsFor(maximum);
          w.Int32(this._colourBits);
          this._Emit(1, 7, w);
          // Components are fractions of the extent the text states, which is nought to this maximum.
          var extent = new CgmCommandWriter();
          for (var i = 0; i < 3; ++i)
            extent.UInt(0, this._colourBits);
          for (var i = 0; i < 3; ++i)
            extent.UInt(maximum, this._colourBits);
          this._Emit(1, 10, extent);
          break;
        }
        case "COLRVALUEEXT":
          this._DirectColour(w, a);
          this._DirectColour(w, a);
          this._Emit(1, 10, w);
          break;
        case "INTEGERPREC" or "REALPREC" or "INDEXPREC" or "COLRINDEXPREC" or "VDCINTEGERPREC" or "VDCREALPREC":
          // The translation keeps its own precisions, wide enough for anything stated.
          break;

        // Picture descriptor.
        case "COLRMODE":
          this._direct = _Keyword(name, a.Word(), "INDEXED", "DIRECT") == 1;
          w.Enum(this._direct ? 1 : 0);
          this._Emit(2, 2, w);
          break;
        case "LINEWIDTHMODE" or "EDGEWIDTHMODE": {
          var mode = _Keyword(name, a.Word(), "ABS|ABSTRACT", "SCALED", "FRACTIONAL", "MM");
          if (name == "LINEWIDTHMODE")
            this._lineWidthAbsolute = mode == 0;
          else
            this._edgeWidthAbsolute = mode == 0;
          w.Enum(mode);
          this._Emit(2, name == "LINEWIDTHMODE" ? 3 : 5, w);
          break;
        }
        case "VDCEXT":
          for (var i = 0; i < 4; ++i)
            this._Vdc(w, a);
          this._Emit(2, 6, w);
          break;
        case "BACKCOLR":
          this._DirectColour(w, a);
          this._Emit(2, 7, w);
          break;

        // Primitives.
        case "LINE" or "DISJTLINE" or "POLYGON":
          while (a.More)
            this._Vdc(w, a);
          this._Emit(4, name switch { "LINE" => 1, "DISJTLINE" => 2, _ => 7 }, w);
          break;
        case "POLYGONSET":
          while (a.More) {
            this._Vdc(w, a);
            this._Vdc(w, a);
            w.Enum(_Keyword(name, a.Word(), "INVIS", "VIS", "CLOSEINVIS", "CLOSEVIS"));
          }

          this._Emit(4, 8, w);
          break;
        case "CELLARRAY": {
          for (var i = 0; i < 6; ++i)
            this._Vdc(w, a);
          var nx = a.Integer();
          var ny = a.Integer();
          w.Int32(nx);
          w.Int32(ny);
          var stated = a.Integer();
          var bits = this._direct ? this._colourBits : 32;
          w.Int32(bits);
          w.Enum(1); // packed
          for (var row = 0; row < ny; ++row) {
            for (var column = 0; column < nx; ++column)
              this._Colour(w, a);
            w.Align();
          }

          _ = stated;
          this._Emit(4, 9, w);
          break;
        }
        case "RECT":
          for (var i = 0; i < 4; ++i)
            this._Vdc(w, a);
          this._Emit(4, 11, w);
          break;
        case "CIRCLE":
          for (var i = 0; i < 3; ++i)
            this._Vdc(w, a);
          this._Emit(4, 12, w);
          break;
        case "ARCCTR":
          for (var i = 0; i < 7; ++i)
            this._Vdc(w, a);
          this._Emit(4, 15, w);
          break;
        case "ARCCTRCLOSE":
          for (var i = 0; i < 7; ++i)
            this._Vdc(w, a);
          w.Enum(_Keyword(name, a.Word(), "PIE", "CHORD"));
          this._Emit(4, 16, w);
          break;
        case "ELLIPSE":
          for (var i = 0; i < 6; ++i)
            this._Vdc(w, a);
          this._Emit(4, 17, w);
          break;

        // Attributes.
        case "LINETYPE" or "HATCHINDEX" or "EDGETYPE":
          w.Int32(a.Integer());
          this._Emit(5, name switch { "LINETYPE" => 2, "HATCHINDEX" => 24, _ => 27 }, w);
          break;
        case "LINEWIDTH" or "EDGEWIDTH":
          if (name == "LINEWIDTH" ? this._lineWidthAbsolute : this._edgeWidthAbsolute)
            this._Vdc(w, a);
          else
            w.Double(a.Number());
          this._Emit(5, name == "LINEWIDTH" ? 3 : 28, w);
          break;
        case "LINECOLR" or "FILLCOLR" or "EDGECOLR":
          this._Colour(w, a);
          this._Emit(5, name switch { "LINECOLR" => 4, "FILLCOLR" => 23, _ => 29 }, w);
          break;
        case "INTSTYLE":
          w.Enum(_Keyword(name, a.Word(), "HOLLOW", "SOLID", "PAT", "HATCH", "EMPTY", "GEOPAT", "INTERP"));
          this._Emit(5, 22, w);
          break;
        case "EDGEVIS":
          w.Enum(_Keyword(name, a.Word(), "OFF", "ON"));
          this._Emit(5, 30, w);
          break;
        case "COLRTABLE":
          w.UInt(a.Integer(), 32);
          while (a.More)
            this._DirectColour(w, a);
          this._Emit(5, 34, w);
          break;

        default:
          // Not drawn here and not changing how anything drawn is read: fonts and text, markers,
          // segments, clipping, escapes, application data and the rest of the descriptors.
          break;
      }
    }
  }
}
