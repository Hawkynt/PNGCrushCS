using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SinclairBasic;

/// <summary>Reads Sinclair BASIC picture programs from bytes, streams, or file paths.</summary>
/// <remarks>
/// The program is walked rather than run. Every statement it may contain is one of a fixed set, and
/// each is matched against the exact token sequence the picture programs use — so a statement that
/// merely resembles one is a rejection, not an approximation. Only PRINT actually puts anything on
/// the screen; the rest is the scrolling bottom line's machinery, which is recognised as a whole
/// and then performed in one step at the NEXT.
/// </remarks>
public static class SinclairBasicReader {

  /// <summary>Ends a line, and at the top level ends the program.</summary>
  private const int _NEWLINE = 118;

  /// <summary>Marks the start and end of a string.</summary>
  private const int _QUOTE = 11;

  public static SinclairBasicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Program not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SinclairBasicFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SinclairBasicFile FromSpan(ReadOnlySpan<byte> data) {
    var state = new _Program(data);
    state.Run();

    return new() { Screen = state.Screen };
  }

  private ref struct _Program {

    private readonly ReadOnlySpan<byte> _data;
    private int _at;
    private int _screenOffset;
    private bool _newLineWorks;

    /// <summary>Where the string the bottom line scrolls begins, or -1 if none was declared.</summary>
    private int _bottomOffset;

    /// <summary>
    /// Which parts of the scrolling bottom line's machinery have been seen. All four have to be
    /// there before the NEXT will draw it, because the four together are what make it a picture
    /// rather than four unrelated statements.
    /// </summary>
    private int _bottomParts;

    public _Program(ReadOnlySpan<byte> data) {
      this._data = data;
      this.Screen = new byte[Zx81Graphics.ScreenSize];
      this._at = SinclairBasicFile.ProgramOffset;
      this._newLineWorks = true;
      this._bottomOffset = -1;
    }

    public byte[] Screen { get; }

    private int _Read() => this._at < this._data.Length ? this._data[this._at++] : -1;

    public void Run() {
      for (;;) {
        // Every line needs at least a number, a length and a terminator after it.
        if (this._at > this._data.Length - 8)
          throw new InvalidDataException("A Sinclair BASIC program ends in the middle of a line.");

        if (this._Read() == _NEWLINE)
          return;

        // The rest of the line number and the two-byte length, none of which a picture needs.
        this._at += 3;

        switch (this._Read()) {
          // REM, RAND, CLS and SLOW change nothing that ends up on the screen.
          case 228 or 229 or 251 or 253:
            break;

          // STOP, RUN and NEW: the picture is whatever has been drawn so far.
          case 227 or 236 or 242:
            return;

          case 245: this._Print(); break;
          case 241: this._Let(); break;
          case 250: this._If(); break;
          case 235: this._For(); break;
          case 244: this._Poke(); break;
          case 243: this._Next(); break;

          default:
            throw new InvalidDataException("A Sinclair BASIC program does more than draw a picture.");
        }

        if (this._Read() != _NEWLINE)
          throw new InvalidDataException("A Sinclair BASIC statement does not end its line.");
      }
    }

    /// <summary>The only statement that puts anything on the screen.</summary>
    private void _Print() {
      for (;;) {
        switch (this._Read()) {
          case _QUOTE:
            this._at = this._PrintString(this._at);
            break;

          // AT row, column
          case 193: {
            var row = this._Number();
            if (row is < 0 or > 21 || this._Read() != 26)
              throw new InvalidDataException("PRINT AT has no row.");

            var column = this._Number();
            if (column is < 0 or > 31)
              throw new InvalidDataException("PRINT AT has no column.");

            this._screenOffset = (row << 5) | column;
            this._newLineWorks = true;
            break;
          }

          // A separator, and the end of an empty PRINT.
          case 0 or 25:
            break;

          case _NEWLINE:
            --this._at;

            // A trailing semicolon suppresses the line break; anything else takes one.
            if (this._data[this._at - 1] != 25) {
              if (this._newLineWorks)
                this._screenOffset = (this._screenOffset & ~31) + 32;

              this._newLineWorks = true;
            }

            return;

          default:
            throw new InvalidDataException("PRINT does more than put a string on the screen.");
        }
      }
    }

    /// <summary>Copies a quoted string to the screen and returns where it ended.</summary>
    private int _PrintString(int offset) {
      for (;;) {
        if (offset >= this._data.Length)
          throw new InvalidDataException("A string never ends.");

        int c = this._data[offset++];
        if (c == _QUOTE)
          return offset;

        if (this._screenOffset >= Zx81Graphics.ScreenSize)
          throw new InvalidDataException("A string runs off the bottom of the screen.");

        // A doubled quote inside a string stands for one quote.
        if (c == 192)
          c = _QUOTE;
        else if ((c & 127) >= 64)
          throw new InvalidDataException("A string holds something that is not a character.");

        this.Screen[this._screenOffset++] = (byte)c;

        // Filling a row exactly has already moved to the next, so a line break would skip one.
        this._newLineWorks = (this._screenOffset & 31) != 0;
      }
    }

    /// <summary>
    /// Reads a number, which the machine stores twice: as the digits it was typed with and then as
    /// a five-byte float, and only the float is read.
    /// </summary>
    private int _Number() {
      for (;;) {
        switch (this._Read()) {
          // The digits and the decimal point, all of which the float that follows supersedes.
          case 21 or 22 or 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 42:
            break;

          case 126: {
            if (this._at > this._data.Length - 5)
              return -1;

            var exponent = this._Read();
            var high = this._Read();
            var low = this._Read();

            // The two least significant bytes of the mantissa cannot affect an integer this small.
            this._at += 2;

            // The sign lives in the mantissa's top bit; a picture never uses a negative number.
            if (exponent > 144 || high >= 128)
              return -1;

            if (exponent <= 128)
              return 0;

            // The mantissa's leading one is implied, and the exponent says how far to shift back.
            return (((high | 128) << 8) | low) >> (144 - exponent);
          }

          default:
            return -1;
        }
      }
    }

    /// <summary>
    /// LET, which declares either the string the bottom line scrolls or one of the two addresses
    /// the scrolling reads.
    /// </summary>
    private void _Let() {
      switch (this._Read()) {
        // The string variable: its contents are not scanned, only found.
        case 38:
          if (this._Read() != 13 || this._Read() != 20 || this._Read() != _QUOTE)
            throw new InvalidDataException("LET does not assign a string.");

          this._bottomOffset = this._at;
          for (;;) {
            var c = this._Read();
            if (c == _QUOTE)
              return;

            if (c < 0)
              throw new InvalidDataException("LET's string never ends.");
          }

        case 56:
          this._bottomParts |= 1;
          this._DPeek(3, 16400);
          return;

        case 41:
          this._bottomParts |= 2;
          this._DPeek(727, 16396);
          return;

        default:
          throw new InvalidDataException("LET assigns something a picture has no use for.");
      }
    }

    /// <summary>
    /// Matches a PEEK of a system variable and the byte above it, which is how BASIC reads an
    /// address the machine stores as two bytes.
    /// </summary>
    private void _DPeek(int expectedValue, int address) {
      if (this._Read() != 20 || this._Number() != expectedValue || this._Read() != 21 || this._Read() != 211
          || this._Number() != address || this._Read() != 21 || this._Number() != 256 || this._Read() != 23
          || this._Read() != 211 || this._Number() != address + 1)
        throw new InvalidDataException($"LET does not read the address at {address}.");
    }

    /// <summary>The one conditional these programs use, which stops when the string is empty.</summary>
    private void _If() {
      if (this._Read() != 198 || this._Read() != 38 || this._Read() != 13 || this._Read() != 221
          || this._Number() != 64 || this._Read() != 222 || this._Read() != 227)
        throw new InvalidDataException("IF is not the one these programs use.");
    }

    private void _For() {
      this._bottomParts |= 4;
      if (this._Read() != 43 || this._Read() != 20 || this._Number() != 0 || this._Read() != 223
          || this._Number() != 63)
        throw new InvalidDataException("FOR is not the one these programs use.");
    }

    private void _Poke() {
      this._bottomParts |= 8;
      ReadOnlySpan<int> expected = [41, 21, 43, 21, 16, 43, 18, -1, 17, 26, 211, 16, 56, 21, 43, 17];

      foreach (var token in expected) {
        if (token < 0) {
          if (this._Number() != 31)
            throw new InvalidDataException("POKE is not the one these programs use.");

          continue;
        }

        if (this._Read() != token)
          throw new InvalidDataException("POKE is not the one these programs use.");
      }
    }

    /// <summary>
    /// The end of the loop, and the point at which the bottom line is actually drawn — once, and
    /// only if every part of the machinery that scrolls it was there.
    /// </summary>
    private void _Next() {
      if (this._Read() != 43 || this._bottomOffset <= 0 || this._bottomParts != 15)
        throw new InvalidDataException("NEXT closes a loop that does not draw anything.");

      this._screenOffset = Zx81Graphics.ScreenSize - Zx81Graphics.Columns * 2;
      this._PrintString(this._bottomOffset);
    }
  }

  public static SinclairBasicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
