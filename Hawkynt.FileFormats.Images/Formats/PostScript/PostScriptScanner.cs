using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FileFormat.PostScript;

/// <summary>Turns the bytes of a PostScript program into objects.</summary>
/// <remarks>
/// The syntax is the one in chapter 3 of the PostScript Language Reference: whitespace separates
/// tokens, the characters <c>( ) &lt; &gt; [ ] { } / %</c> are self-delimiting, a percent sign runs
/// to the end of the line, a slash quotes a name, parentheses quote a string with escapes, angle
/// brackets quote one in hexadecimal, and braces gather a procedure.
/// <para/>
/// Scanning reads from the same file object the program is running out of, and that is deliberate
/// rather than incidental: <c>currentfile</c> hands a program its own source, and a program that
/// writes image data into its own text and reads it back with <c>readhexstring</c> only works if the
/// reader is exactly where the scanner stopped. Holding a position in a shared file gives that for
/// nothing; scanning the whole program up front would not.
/// </remarks>
public static class PostScriptScanner {

  /// <summary>How deep braces may nest before the program is refused as malformed.</summary>
  private const int _MaxProcedureDepth = 96;

  /// <summary>The longest a single token may be, which bounds what a runaway string can cost.</summary>
  private const int _MaxTokenLength = 1 << 24;

  /// <summary>Whether the byte separates tokens.</summary>
  public static bool IsWhitespace(int c) => c is 0 or 9 or 10 or 12 or 13 or 32;

  /// <summary>Whether the byte ends a token by itself.</summary>
  public static bool IsDelimiter(int c) => c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

  /// <summary>
  /// The next object in the program, or nothing at the end of it.
  /// </summary>
  /// <param name="file">The program, whose position moves past whatever is read.</param>
  public static PsObject? Next(PsFile file) => _Next(file, 0);

  private static PsObject? _Next(PsFile file, int depth) {
    for (;;) {
      var c = _SkipSpace(file);
      if (c < 0)
        return null;

      switch (c) {
        case '%':
          _SkipComment(file);
          continue;

        case '/':
          return _Name(file);

        case '(':
          return PsObject.FromString(_Text(file));

        case '<':
          return _AngleBracket(file);

        case '>':
          if (file.PeekByte() == '>') {
            file.ReadByte();
            return PsObject.FromExecutableName(">>");
          }

          throw new InvalidDataException("A '>' in a PostScript program that closes nothing.");

        case '{':
          return PsObject.FromProcedure(_Procedure(file, depth + 1));

        case '}':
          throw new InvalidDataException("A '}' in a PostScript program that closes nothing.");

        case '[':
          return PsObject.FromExecutableName("[");

        case ']':
          return PsObject.FromExecutableName("]");

        case ')':
          throw new InvalidDataException("A ')' in a PostScript program that closes nothing.");

        default:
          return _Word(file, c);
      }
    }
  }

  private static int _SkipSpace(PsFile file) {
    for (;;) {
      var c = file.ReadByte();
      if (c < 0)
        return -1;

      if (!IsWhitespace(c))
        return c;
    }
  }

  private static void _SkipComment(PsFile file) {
    for (;;) {
      var c = file.ReadByte();
      if (c < 0 || c == '\n' || c == '\r')
        return;
    }
  }

  /// <summary>A name after a slash, or the immediately evaluated name after two of them.</summary>
  private static PsObject _Name(PsFile file) {
    if (file.PeekByte() == '/') {
      file.ReadByte();

      // Two slashes ask for the value the name has now rather than the name. Only the interpreter
      // can look that up, so the request is carried as a name of its own and resolved there.
      return PsObject.FromExecutableName("//" + _Word(file));
    }

    return PsObject.FromName(_Word(file));
  }

  /// <summary>
  /// A run of characters up to the first delimiter, with a terminating space taken with it.
  /// </summary>
  /// <remarks>
  /// The reference says the whitespace character that ends a token is consumed as part of it, and
  /// that is not a detail either: a program that reads its own text with <c>readline</c> after an
  /// operator gets the rest of that line only if the newline after the operator has been taken. A
  /// carriage return and a line feed together are one such character, which is what a file written
  /// on a different machine leaves behind.
  /// </remarks>
  private static string _Word(PsFile file) {
    var text = new List<byte>();
    for (;;) {
      var c = file.PeekByte();
      if (c < 0 || IsWhitespace(c) || IsDelimiter(c))
        break;

      file.ReadByte();
      text.Add((byte)c);
      if (text.Count > _MaxTokenLength)
        throw new InvalidDataException("A name in a PostScript program longer than any name can be.");
    }

    if (IsWhitespace(file.PeekByte())) {
      var space = file.ReadByte();
      if (space == '\r' && file.PeekByte() == '\n')
        file.ReadByte();
    }

    return System.Text.Encoding.Latin1.GetString(text.ToArray());
  }

  /// <summary>A bare word, which is a number if it reads as one and an executable name otherwise.</summary>
  private static PsObject _Word(PsFile file, int first) {
    var text = (char)first + _Word(file);
    return _Number(text) ?? PsObject.FromExecutableName(text);
  }

  /// <summary>
  /// A word read as a number, or nothing when it is not one.
  /// </summary>
  /// <remarks>
  /// Three forms: an integer, a real with a point or an exponent, and a radix number written
  /// <c>base#digits</c> for bases two to thirty-six. Anything else is a name, including the words
  /// that look numeric but overflow — those are refused rather than silently wrapped.
  /// </remarks>
  private static PsObject? _Number(string text) {
    if (text.Length == 0)
      return null;

    var hash = text.IndexOf('#');
    if (hash > 0) {
      if (!int.TryParse(text.AsSpan(0, hash), NumberStyles.None, CultureInfo.InvariantCulture, out var radix) || radix is < 2 or > 36)
        return null;

      var digits = text.AsSpan(hash + 1);
      if (digits.Length == 0)
        return null;

      long value = 0;
      foreach (var digit in digits) {
        var d = digit switch {
          >= '0' and <= '9' => digit - '0',
          >= 'a' and <= 'z' => digit - 'a' + 10,
          >= 'A' and <= 'Z' => digit - 'A' + 10,
          _ => -1
        };

        if (d < 0 || d >= radix)
          return null;

        value = value * radix + d;
      }

      return PsObject.FromInteger(value);
    }

    var wantsReal = false;
    for (var i = 0; i < text.Length; ++i) {
      var c = text[i];
      if (c is '.' or 'e' or 'E') {
        wantsReal = true;
        continue;
      }

      if (c is '+' or '-' || char.IsAsciiDigit(c))
        continue;

      return null;
    }

    if (!wantsReal && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
      return PsObject.FromInteger(integer);

    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && double.IsFinite(real))
      return PsObject.FromReal(real);

    // A word that is all digits and signs but reads as neither is a number too big to hold. Taking
    // it as a name would leave it undefined at the point it is used, which is the right refusal.
    return null;
  }

  /// <summary>
  /// A string in parentheses.
  /// </summary>
  /// <remarks>
  /// Balanced parentheses nest without escaping. A backslash escapes the usual control characters,
  /// takes up to three octal digits as one byte, and before a newline joins the two lines into one
  /// string with no character between them. A bare carriage return, or a carriage return and line
  /// feed, is one line feed in the string.
  /// </remarks>
  private static PsString _Text(PsFile file) {
    var bytes = new List<byte>();
    var depth = 1;

    for (;;) {
      var c = file.ReadByte();
      if (c < 0)
        throw new InvalidDataException("A string in a PostScript program that is never closed.");

      if (bytes.Count > _MaxTokenLength)
        throw new InvalidDataException("A string in a PostScript program longer than any string can be.");

      switch (c) {
        case '(':
          ++depth;
          bytes.Add((byte)c);
          continue;

        case ')':
          if (--depth == 0)
            return new(bytes.ToArray(), 0, bytes.Count);

          bytes.Add((byte)c);
          continue;

        case '\r':
          if (file.PeekByte() == '\n')
            file.ReadByte();

          bytes.Add((byte)'\n');
          continue;

        case '\\':
          _Escape(file, bytes);
          continue;

        default:
          bytes.Add((byte)c);
          continue;
      }
    }
  }

  private static void _Escape(PsFile file, List<byte> bytes) {
    var c = file.ReadByte();
    switch (c) {
      case < 0:
        throw new InvalidDataException("A string in a PostScript program that ends inside an escape.");

      case 'n':
        bytes.Add((byte)'\n');
        return;

      case 'r':
        bytes.Add((byte)'\r');
        return;

      case 't':
        bytes.Add((byte)'\t');
        return;

      case 'b':
        bytes.Add(8);
        return;

      case 'f':
        bytes.Add(12);
        return;

      case '\r':
        if (file.PeekByte() == '\n')
          file.ReadByte();

        return;

      case '\n':
        return;

      case >= '0' and <= '7': {
        var value = c - '0';
        for (var i = 0; i < 2; ++i) {
          var next = file.PeekByte();
          if (next is < '0' or > '7')
            break;

          file.ReadByte();
          value = value * 8 + (next - '0');
        }

        bytes.Add((byte)value);
        return;
      }

      default:
        bytes.Add((byte)c);
        return;
    }
  }

  /// <summary>A hexadecimal string, an ASCII85 string, or the <c>&lt;&lt;</c> that opens a dictionary.</summary>
  private static PsObject _AngleBracket(PsFile file) {
    var next = file.PeekByte();
    if (next == '<') {
      file.ReadByte();
      return PsObject.FromExecutableName("<<");
    }

    if (next == '~') {
      file.ReadByte();
      return PsObject.FromString(_Ascii85(file));
    }

    return PsObject.FromString(_Hex(file));
  }

  /// <summary>
  /// A hexadecimal string.
  /// </summary>
  /// <remarks>
  /// Whitespace between digits is ignored and an odd number of digits is completed with a zero,
  /// which the reference states. A character that is neither a digit nor whitespace is an error
  /// rather than something to pass over: it means the string is not what it claims to be.
  /// </remarks>
  private static PsString _Hex(PsFile file) {
    var bytes = new List<byte>();
    var high = -1;

    for (;;) {
      var c = file.ReadByte();
      if (c < 0)
        throw new InvalidDataException("A hexadecimal string in a PostScript program that is never closed.");

      if (c == '>') {
        if (high >= 0)
          bytes.Add((byte)(high << 4));

        return new(bytes.ToArray(), 0, bytes.Count);
      }

      if (IsWhitespace(c))
        continue;

      var digit = _HexDigit(c);
      if (digit < 0)
        throw new InvalidDataException($"The character '{(char)c}' inside a hexadecimal string in a PostScript program.");

      if (high < 0)
        high = digit;
      else {
        bytes.Add((byte)((high << 4) | digit));
        high = -1;
      }

      if (bytes.Count > _MaxTokenLength)
        throw new InvalidDataException("A hexadecimal string in a PostScript program longer than any string can be.");
    }
  }

  private static int _HexDigit(int c) => c switch {
    >= '0' and <= '9' => c - '0',
    >= 'a' and <= 'f' => c - 'a' + 10,
    >= 'A' and <= 'F' => c - 'A' + 10,
    _ => -1
  };

  /// <summary>An ASCII85 string, as Level 2 writes one between <c>&lt;~</c> and <c>~&gt;</c>.</summary>
  private static PsString _Ascii85(PsFile file) {
    var bytes = new List<byte>();
    var group = new int[5];
    var have = 0;

    for (;;) {
      var c = file.ReadByte();
      if (c < 0)
        throw new InvalidDataException("An ASCII85 string in a PostScript program that is never closed.");

      if (IsWhitespace(c))
        continue;

      if (c == '~') {
        if (file.PeekByte() == '>')
          file.ReadByte();

        if (have > 0) {
          if (have == 1)
            throw new InvalidDataException("An ASCII85 string in a PostScript program ending on a single character, which encodes nothing.");

          for (var i = have; i < 5; ++i)
            group[i] = 84;

          _Ascii85Group(group, bytes, have - 1);
        }

        return new(bytes.ToArray(), 0, bytes.Count);
      }

      if (c == 'z' && have == 0) {
        bytes.AddRange([0, 0, 0, 0]);
        continue;
      }

      if (c is < '!' or > 'u')
        throw new InvalidDataException($"The character '{(char)c}' inside an ASCII85 string in a PostScript program.");

      group[have++] = c - '!';
      if (have < 5)
        continue;

      _Ascii85Group(group, bytes, 4);
      have = 0;

      if (bytes.Count > _MaxTokenLength)
        throw new InvalidDataException("An ASCII85 string in a PostScript program longer than any string can be.");
    }
  }

  private static void _Ascii85Group(int[] group, List<byte> bytes, int count) {
    var value = 0L;
    for (var i = 0; i < 5; ++i)
      value = value * 85 + group[i];

    for (var i = 0; i < count; ++i)
      bytes.Add((byte)(value >> (24 - i * 8)));
  }

  /// <summary>Everything between a brace and its match, as a procedure.</summary>
  private static PsArray _Procedure(PsFile file, int depth) {
    if (depth > _MaxProcedureDepth)
      throw new InvalidDataException($"Procedures in a PostScript program nested more than {_MaxProcedureDepth} deep.");

    var items = new List<PsObject>();
    for (;;) {
      var c = _SkipSpace(file);
      if (c < 0)
        throw new InvalidDataException("A procedure in a PostScript program that is never closed.");

      switch (c) {
        case '}':
          return new(items);

        case '%':
          _SkipComment(file);
          continue;

        case '{':
          items.Add(PsObject.FromProcedure(_Procedure(file, depth + 1)));
          continue;

        case '/':
          items.Add(_Name(file));
          continue;

        case '(':
          items.Add(PsObject.FromString(_Text(file)));
          continue;

        case '<':
          items.Add(_AngleBracket(file));
          continue;

        case '>':
          if (file.PeekByte() != '>')
            throw new InvalidDataException("A '>' in a PostScript procedure that closes nothing.");

          file.ReadByte();
          items.Add(PsObject.FromExecutableName(">>"));
          continue;

        case '[':
          items.Add(PsObject.FromExecutableName("["));
          continue;

        case ']':
          items.Add(PsObject.FromExecutableName("]"));
          continue;

        case ')':
          throw new InvalidDataException("A ')' in a PostScript procedure that closes nothing.");

        default:
          items.Add(_Word(file, c));
          continue;
      }
    }
  }
}
