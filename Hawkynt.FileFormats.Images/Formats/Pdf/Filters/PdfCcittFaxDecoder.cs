using System;
using System.IO;

namespace FileFormat.Pdf.Filters;

/// <summary>The PDF <c>CCITTFaxDecode</c> filter: ITU-T T.4 (Group 3, one- and two-dimensional) and T.6 (Group 4).</summary>
/// <remarks>
/// Converted from pdf.js <c>src/core/ccitt.js</c> (Apache-2.0), itself a port of xpdf's
/// <c>CCITTFaxStream</c> that Glyph &amp; Cog made available under Apache 2.0; see
/// <c>THIRD-PARTY-NOTICE.pdfjs.txt</c> beside this file. The code tables are copied value for
/// value.
/// <para/>
/// A row is held as the list of positions where its colour changes (<c>_codingLine</c>), starting
/// white, which is what both the one-dimensional run lengths and the two-dimensional modes are
/// stated against. The output is what the filter yields in PDF: one bit a pixel, rows padded to a
/// byte, and a 1 bit white unless <c>BlackIs1</c> says otherwise.
/// <para/>
/// Damaged data is tolerated the way the reference does it: an unknown code ends the row as white,
/// and with <c>EndOfLine</c> set the decoder resynchronises on the next end-of-line marker.
/// <para/>
/// Three details follow xpdf and poppler, the oracle, rather than pdf.js: a row the data ends
/// straight after is still emitted, no byte alignment is applied after a row that began with an
/// end-of-line marker (TIFF-style fill puts the padding before the next marker instead), and data
/// that opens with an end-of-line marker is read as carrying one on every row.
/// </remarks>
internal static class PdfCcittFaxDecoder {

  /// <summary>Decodes a <c>CCITTFaxDecode</c> stream with the parameters of its decode dictionary.</summary>
  /// <param name="data">The encoded bytes.</param>
  /// <param name="k">
  /// The <c>K</c> entry: negative for Group 4, zero for Group 3 one-dimensional, positive for
  /// Group 3 with two-dimensional rows mixed in.
  /// </param>
  /// <param name="endOfLine">Whether every row is required to start with an end-of-line marker.</param>
  /// <param name="encodedByteAlign">Whether each row (or each end-of-line marker) starts on a byte boundary.</param>
  /// <param name="columns">The width in pixels; zero means the default of 1728.</param>
  /// <param name="rows">The height in pixels, or zero to decode until the data ends.</param>
  /// <param name="endOfBlock">Whether the data ends with an end-of-block pattern rather than after <paramref name="rows"/> rows.</param>
  /// <param name="blackIs1">Whether a 1 bit means black in the output.</param>
  /// <returns>
  /// Packed rows, most significant bit first, <c>(columns + 7) / 8</c> bytes each. When
  /// <paramref name="rows"/> is given exactly that many rows come back, the missing ones white.
  /// </returns>
  internal static byte[] Decode(byte[] data, int k, bool endOfLine, bool encodedByteAlign, int columns, int rows, bool endOfBlock, bool blackIs1) {
    ArgumentNullException.ThrowIfNull(data);
    if (columns <= 0)
      columns = 1728;
    if (rows < 0)
      rows = 0;
    if (columns > _MaximumColumns)
      throw new InvalidDataException($"A CCITT image {columns} pixels wide is not an image.");

    var bytesPerRow = (columns + 7) / 8;
    if ((long)rows * bytesPerRow > _MaximumOutput)
      throw new InvalidDataException($"A CCITT image of {columns}x{rows} is larger than this will decode.");

    var state = new _State(data, k, endOfLine, encodedByteAlign, columns, rows, endOfBlock, blackIs1);
    var capacity = rows > 0 ? rows * bytesPerRow : Math.Max(bytesPerRow, (int)Math.Min(data.Length * 4L, 1 << 20));
    var output = new byte[capacity];
    var length = 0;

    for (int c; (c = state.ReadNextChar()) >= 0;) {
      if (length == output.Length) {
        if (rows > 0)
          break;
        if (output.Length >= _MaximumOutput)
          throw new InvalidDataException("CCITT data decodes to more rows than this will hold.");

        Array.Resize(ref output, (int)Math.Min(output.Length * 2L, _MaximumOutput));
      }

      output[length++] = (byte)c;
    }

    if (rows == 0)
      return output.AsSpan(0, length - length % bytesPerRow).ToArray();

    // Rows the data never reached are white, which is what an unmarked page is.
    if (length < output.Length)
      output.AsSpan(length).Fill(blackIs1 ? (byte)0x00 : (byte)0xFF);

    return output;
  }

  /// <summary>
  /// Decodes an MMR-coded JBIG2 bitmap (T.88 6.2.6), which is Group 4 coding with 1 meaning black.
  /// </summary>
  /// <param name="data">The buffer holding the coded data.</param>
  /// <param name="start">Where the coded data begins.</param>
  /// <param name="end">Where the coded data must end.</param>
  /// <param name="width">The bitmap's width.</param>
  /// <param name="height">The bitmap's height.</param>
  /// <param name="endOfBlock">Whether the bitmap is terminated by an end-of-facsimile-block code.</param>
  /// <param name="next">
  /// The first byte after the bitmap: past its end-of-block when it has one, and rounded up to a
  /// byte, which is where the next of several bitmaps coded back to back begins.
  /// </param>
  /// <returns>One array a row, one byte a pixel, 1 for black.</returns>
  internal static byte[][] DecodeMmrBitmap(byte[] data, int start, int end, int width, int height, bool endOfBlock, out int next) {
    ArgumentNullException.ThrowIfNull(data);
    var state = new _State(data, start, end, -1, false, false, Math.Max(width, 1), height, endOfBlock, true);
    var bitmap = new byte[height][];
    var exhausted = false;

    for (var y = 0; y < height; ++y) {
      var row = bitmap[y] = new byte[width];
      var shift = -1;
      var current = 0;
      for (var x = 0; x < width; ++x) {
        if (shift < 0) {
          current = exhausted ? -1 : state.ReadNextChar();
          if (current < 0) {
            // What the data does not reach stays white.
            current = 0;
            exhausted = true;
          }

          shift = 7;
        }

        row[x] = (byte)((current >> shift) & 1);
        --shift;
      }
    }

    next = state.EndOfBitmap(endOfBlock);
    return bitmap;
  }

  /// <summary>The widest row decoded, which no fax or scan comes near.</summary>
  private const int _MaximumColumns = 1 << 20;

  /// <summary>The most output bytes a stream may decode to, so a stream stating a silly size cannot exhaust memory.</summary>
  private const int _MaximumOutput = 1 << 30;

  private const int _Eof = -1;
  private const int _TwoDimPass = 0;
  private const int _TwoDimHorizontal = 1;
  private const int _TwoDimVertical0 = 2;
  private const int _TwoDimVerticalRight1 = 3;
  private const int _TwoDimVerticalLeft1 = 4;
  private const int _TwoDimVerticalRight2 = 5;
  private const int _TwoDimVerticalLeft2 = 6;
  private const int _TwoDimVerticalRight3 = 7;
  private const int _TwoDimVerticalLeft3 = 8;

  /// <summary>One decode in progress: the bit reader, the two change lists and the row being emitted.</summary>
  private sealed class _State {

    private readonly byte[] _data;
    private readonly int _dataEnd;
    private int _dataPosition;

    private readonly int _encoding;
    private bool _endOfLine;
    private readonly bool _byteAlign;
    private readonly int _columns;
    private readonly int _rows;
    private readonly bool _endOfBlock;
    private readonly bool _black;

    private readonly int[] _codingLine;
    private readonly int[] _refLine;
    private int _codingPos;
    private int _row;
    private bool _nextLine2D;
    private int _inputBits;
    private int _inputBuffer;
    private int _outputBits;
    private bool _rowsDone;
    private bool _eof;
    private bool _error;
    private bool _rowStartedWithEol;
    private int _rowBytesLeft;

    public _State(byte[] data, int k, bool endOfLine, bool encodedByteAlign, int columns, int rows, bool endOfBlock, bool blackIs1)
      : this(data, 0, data.Length, k, endOfLine, encodedByteAlign, columns, rows, endOfBlock, blackIs1) { }

    public _State(byte[] data, int start, int end, int k, bool endOfLine, bool encodedByteAlign, int columns, int rows, bool endOfBlock, bool blackIs1) {
      this._data = data;
      this._dataPosition = start;
      this._dataEnd = Math.Min(end, data.Length);
      this._encoding = k;
      this._endOfLine = endOfLine;
      this._byteAlign = encodedByteAlign;
      this._columns = columns;
      this._rows = rows;
      this._endOfBlock = endOfBlock;
      this._black = blackIs1;

      this._codingLine = new int[columns + 1];
      this._refLine = new int[columns + 2];
      this._codingLine[0] = columns;
      this._nextLine2D = k < 0;

      // Fill bits and a leading end-of-line marker come before the first row.
      int code;
      while ((code = this._LookBits(12)) == 0)
        this._EatBits(1);

      // Data that opens with an end-of-line marker has them on every row, whatever EndOfLine says,
      // which is how poppler reads it too.
      if (code == 1) {
        this._EatBits(12);
        this._rowStartedWithEol = true;
        this._endOfLine = true;
      }

      if (k > 0) {
        this._nextLine2D = this._LookBits(1) == 0;
        this._EatBits(1);
      }
    }

    /// <summary>The next output byte, or -1 once the data has ended.</summary>
    public int ReadNextChar() {
      // The data running out after a row still lets that row out. pdf.js stops at once and so
      // loses the last row of data that ends without a marker; poppler keeps it.
      if (this._eof && this._rowBytesLeft == 0)
        return -1;

      var codingLine = this._codingLine;
      var columns = this._columns;

      if (this._outputBits == 0) {
        if (this._rowsDone)
          this._eof = true;
        if (this._eof)
          return -1;

        this._error = false;
        if (this._nextLine2D)
          this._DecodeTwoDimensionalRow();
        else
          this._DecodeOneDimensionalRow();

        // Running out while hunting for the marker after a damaged row loses that row, as in the reference.
        if (!this._FinishRow())
          return -1;

        this._codingPos = codingLine[0] > 0 ? 0 : 1;
        this._outputBits = codingLine[this._codingPos];
        this._rowBytesLeft = (columns + 7) >> 3;
        ++this._row;
      }

      --this._rowBytesLeft;

      int c;
      if (this._outputBits >= 8) {
        c = (this._codingPos & 1) != 0 ? 0 : 0xFF;
        this._outputBits -= 8;
        if (this._outputBits == 0 && codingLine[this._codingPos] < columns) {
          ++this._codingPos;
          this._outputBits = codingLine[this._codingPos] - codingLine[this._codingPos - 1];
        }
      } else {
        var bits = 8;
        c = 0;
        do {
          if (this._outputBits > bits) {
            c <<= bits;
            if ((this._codingPos & 1) == 0)
              c |= 0xFF >> (8 - bits);

            this._outputBits -= bits;
            bits = 0;
          } else {
            c <<= this._outputBits;
            if ((this._codingPos & 1) == 0)
              c |= 0xFF >> (8 - this._outputBits);

            bits -= this._outputBits;
            this._outputBits = 0;
            if (codingLine[this._codingPos] < columns) {
              ++this._codingPos;
              this._outputBits = codingLine[this._codingPos] - codingLine[this._codingPos - 1];
            } else if (bits > 0) {
              c <<= bits;
              bits = 0;
            }
          }
        } while (bits != 0);
      }

      if (this._black)
        c ^= 0xFF;

      return c & 0xFF;
    }

    /// <summary>
    /// Consumes what is left of an end-of-facsimile-block after the last row (whose first
    /// end-of-line the row's own trailer has already taken) and says where the next byte-aligned
    /// data begins.
    /// </summary>
    public int EndOfBitmap(bool endOfBlock) {
      if (endOfBlock && this._LookBits(12) == 1)
        this._EatBits(12);

      return this._dataPosition - this._inputBits / 8;
    }

    private void _DecodeTwoDimensionalRow() {
      var refLine = this._refLine;
      var codingLine = this._codingLine;
      var columns = this._columns;

      int i;
      for (i = 0; codingLine[i] < columns; ++i)
        refLine[i] = codingLine[i];
      refLine[i++] = columns;
      refLine[i] = columns;

      codingLine[0] = 0;
      this._codingPos = 0;
      var refPos = 0;
      var blackPixels = 0;

      while (codingLine[this._codingPos] < columns) {
        var mode = this._GetTwoDimCode();
        switch (mode) {
          case _TwoDimPass:
            this._AddPixels(this._Ref(refPos + 1), blackPixels);
            if (this._Ref(refPos + 1) < columns)
              refPos += 2;
            break;

          case _TwoDimHorizontal: {
            int first = 0, second = 0, part;
            if (blackPixels != 0) {
              do first += part = this._GetBlackCode(); while (part >= 64);
              do second += part = this._GetWhiteCode(); while (part >= 64);
            } else {
              do first += part = this._GetWhiteCode(); while (part >= 64);
              do second += part = this._GetBlackCode(); while (part >= 64);
            }

            this._AddPixels(codingLine[this._codingPos] + first, blackPixels);
            if (codingLine[this._codingPos] < columns)
              this._AddPixels(codingLine[this._codingPos] + second, blackPixels ^ 1);

            refPos = this._SkipReferenceChanges(refPos);
            break;
          }

          case _TwoDimVerticalRight3:
          case _TwoDimVerticalRight2:
          case _TwoDimVerticalRight1:
          case _TwoDimVertical0: {
            var offset = mode switch {
              _TwoDimVerticalRight3 => 3,
              _TwoDimVerticalRight2 => 2,
              _TwoDimVerticalRight1 => 1,
              _ => 0,
            };
            this._AddPixels(this._Ref(refPos) + offset, blackPixels);
            blackPixels ^= 1;
            if (codingLine[this._codingPos] < columns)
              refPos = this._SkipReferenceChanges(refPos + 1);

            break;
          }

          case _TwoDimVerticalLeft3:
          case _TwoDimVerticalLeft2:
          case _TwoDimVerticalLeft1: {
            var offset = mode switch {
              _TwoDimVerticalLeft3 => 3,
              _TwoDimVerticalLeft2 => 2,
              _ => 1,
            };
            this._AddPixelsNegative(this._Ref(refPos) - offset, blackPixels);
            blackPixels ^= 1;
            if (codingLine[this._codingPos] < columns)
              refPos = this._SkipReferenceChanges(refPos > 0 ? refPos - 1 : refPos + 1);

            break;
          }

          case _Eof:
            this._AddPixels(columns, 0);
            this._eof = true;
            break;

          default:
            // An unknown mode: the rest of the row is taken as white and the row marked damaged.
            this._AddPixels(columns, 0);
            this._error = true;
            break;
        }
      }
    }

    private int _SkipReferenceChanges(int refPos) {
      var position = this._codingLine[this._codingPos];
      while (this._Ref(refPos) <= position && this._Ref(refPos) < this._columns)
        refPos += 2;

      return refPos;
    }

    /// <summary>The reference line at an index, which past its end reads as the right-hand edge.</summary>
    private int _Ref(int index) => (uint)index < (uint)this._refLine.Length ? this._refLine[index] : this._columns;

    private void _DecodeOneDimensionalRow() {
      var codingLine = this._codingLine;
      codingLine[0] = 0;
      this._codingPos = 0;
      var blackPixels = 0;
      while (codingLine[this._codingPos] < this._columns) {
        int run = 0, part;
        if (blackPixels != 0)
          do run += part = this._GetBlackCode(); while (part >= 64);
        else
          do run += part = this._GetWhiteCode(); while (part >= 64);

        this._AddPixels(codingLine[this._codingPos] + run, blackPixels);
        blackPixels ^= 1;
      }
    }

    /// <summary>
    /// Reads what follows a row: alignment, end-of-line, the next row's mode bit, end-of-block.
    /// False when the data ran out while resynchronising after a damaged row.
    /// </summary>
    private bool _FinishRow() {
      var gotEol = false;
      int code;

      // A row that began with an end-of-line marker is not padded at its end: the fill bits come
      // before the next marker, which the search below skips anyway. Aligning there would land
      // inside that marker. pdf.js aligns regardless; xpdf and poppler do not, and Adobe agrees.
      if (this._byteAlign && !this._rowStartedWithEol)
        this._inputBits &= ~7;

      if (!this._endOfBlock && this._row == this._rows - 1)
        this._rowsDone = true;
      else {
        code = this._LookBits(12);
        if (this._endOfLine) {
          while (code != _Eof && code != 1) {
            this._EatBits(1);
            code = this._LookBits(12);
          }
        } else {
          while (code == 0) {
            this._EatBits(1);
            code = this._LookBits(12);
          }
        }

        if (code == 1) {
          this._EatBits(12);
          gotEol = true;
        } else if (code == _Eof)
          this._eof = true;
      }

      this._rowStartedWithEol = gotEol;

      if (!this._eof && this._encoding > 0 && !this._rowsDone) {
        this._nextLine2D = this._LookBits(1) == 0;
        this._EatBits(1);
      }

      if (this._endOfBlock && gotEol && this._byteAlign) {
        // A second end-of-line straight after the first is the start of the return-to-control
        // sequence, which ends the data.
        code = this._LookBits(12);
        if (code == 1) {
          this._EatBits(12);
          if (this._encoding > 0) {
            this._LookBits(1);
            this._EatBits(1);
          }

          if (this._encoding >= 0)
            for (var i = 0; i < 4; ++i) {
              this._LookBits(12);
              this._EatBits(12);
              if (this._encoding > 0) {
                this._LookBits(1);
                this._EatBits(1);
              }
            }

          this._eof = true;
        }
      } else if (this._error && this._endOfLine) {
        // Resynchronise on the next end-of-line marker.
        while (true) {
          code = this._LookBits(13);
          if (code == _Eof) {
            this._eof = true;
            return false;
          }

          if (code >> 1 == 1)
            break;

          this._EatBits(1);
        }

        this._EatBits(12);
        this._rowStartedWithEol = true;
        if (this._encoding > 0) {
          this._EatBits(1);
          this._nextLine2D = (code & 1) == 0;
        }
      }

      return true;
    }

    private void _AddPixels(int a1, int blackPixels) {
      var codingLine = this._codingLine;
      var codingPos = this._codingPos;

      if (a1 > codingLine[codingPos]) {
        if (a1 > this._columns) {
          this._error = true;
          a1 = this._columns;
        }

        if (((codingPos & 1) ^ blackPixels) != 0)
          ++codingPos;

        codingLine[codingPos] = a1;
      }

      this._codingPos = codingPos;
    }

    private void _AddPixelsNegative(int a1, int blackPixels) {
      var codingLine = this._codingLine;
      var codingPos = this._codingPos;

      if (a1 > codingLine[codingPos]) {
        if (a1 > this._columns) {
          this._error = true;
          a1 = this._columns;
        }

        if (((codingPos & 1) ^ blackPixels) != 0)
          ++codingPos;

        codingLine[codingPos] = a1;
      } else if (a1 < codingLine[codingPos]) {
        if (a1 < 0) {
          this._error = true;
          a1 = 0;
        }

        while (codingPos > 0 && a1 < codingLine[codingPos - 1])
          --codingPos;

        codingLine[codingPos] = a1;
      }

      this._codingPos = codingPos;
    }

    /// <summary>
    /// Tries each code length from <paramref name="start"/> to <paramref name="end"/> against a table
    /// indexed by codes left-aligned to <paramref name="end"/> bits, which is how the codes are looked
    /// up when the data is not trusted to carry an end-of-block.
    /// </summary>
    private (bool Found, int Value, bool Consumed) _FindTableCode(int start, int end, short[] table, int limit) {
      for (var i = start; i <= end; ++i) {
        var code = this._LookBits(i);
        if (code == _Eof)
          return (true, 1, false);

        if (i < end)
          code <<= end - i;

        if (limit == 0 || code >= limit) {
          var index = (code - limit) * 2;
          if (index + 1 < table.Length && table[index] == i) {
            this._EatBits(i);
            return (true, table[index + 1], true);
          }
        }
      }

      return (false, 0, false);
    }

    private int _GetTwoDimCode() {
      if (this._endOfBlock) {
        var code = this._LookBits(7);
        if (code >= 0 && _TwoDimTable[code * 2] > 0) {
          this._EatBits(_TwoDimTable[code * 2]);
          return _TwoDimTable[code * 2 + 1];
        }
      } else {
        var (found, value, consumed) = this._FindTableCode(1, 7, _TwoDimTable, 0);
        if (found && consumed)
          return value;
      }

      return _Eof;
    }

    private int _GetWhiteCode() {
      if (this._endOfBlock) {
        var code = this._LookBits(12);
        if (code == _Eof)
          return 1;

        var (table, index) = code >> 5 == 0 ? (_WhiteTable1, code) : (_WhiteTable2, code >> 3);
        if (table[index * 2] > 0) {
          this._EatBits(table[index * 2]);
          return table[index * 2 + 1];
        }
      } else {
        var result = this._FindTableCode(1, 9, _WhiteTable2, 0);
        if (result.Found)
          return result.Value;

        result = this._FindTableCode(11, 12, _WhiteTable1, 0);
        if (result.Found)
          return result.Value;
      }

      this._EatBits(1);
      return 1;
    }

    private int _GetBlackCode() {
      if (this._endOfBlock) {
        var code = this._LookBits(13);
        if (code == _Eof)
          return 1;

        var (table, index) = code >> 7 == 0
          ? (_BlackTable1, code)
          : code >> 9 == 0
            ? (_BlackTable2, (code >> 1) - 64)
            : (_BlackTable3, code >> 7);

        if (table[index * 2] > 0) {
          this._EatBits(table[index * 2]);
          return table[index * 2 + 1];
        }
      } else {
        var result = this._FindTableCode(2, 6, _BlackTable3, 0);
        if (result.Found)
          return result.Value;

        result = this._FindTableCode(7, 12, _BlackTable2, 64);
        if (result.Found)
          return result.Value;

        result = this._FindTableCode(10, 13, _BlackTable1, 0);
        if (result.Found)
          return result.Value;
      }

      this._EatBits(1);
      return 1;
    }

    /// <summary>
    /// The next <paramref name="n"/> bits without consuming them, zero-padded at the end of the
    /// data, or <see cref="_Eof"/> once nothing is left.
    /// </summary>
    private int _LookBits(int n) {
      while (this._inputBits < n) {
        if (this._dataPosition >= this._dataEnd) {
          if (this._inputBits == 0)
            return _Eof;

          return (this._inputBuffer << (n - this._inputBits)) & (0xFFFF >> (16 - n));
        }

        this._inputBuffer = ((this._inputBuffer << 8) | this._data[this._dataPosition++]) & 0xFFFFFF;
        this._inputBits += 8;
      }

      return (this._inputBuffer >> (this._inputBits - n)) & (0xFFFF >> (16 - n));
    }

    private void _EatBits(int n) {
      if ((this._inputBits -= n) < 0)
        this._inputBits = 0;
    }
  }

  #region code tables, copied value for value from pdf.js ccitt.js

  /// <summary><c>twoDimTable</c>: 128 entries of (code length, value) pairs, flattened.</summary>
  private static readonly short[] _TwoDimTable = [
    -1, -1, -1, -1, 7, 8, 7, 7, 6, 6, 6, 6, 6, 5, 6, 5,
    4, 0, 4, 0, 4, 0, 4, 0, 4, 0, 4, 0, 4, 0, 4, 0,
    3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1,
    3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1,
    3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4,
    3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4,
    3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
    1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
  ];

  /// <summary><c>whiteTable1</c>: 32 entries of (code length, value) pairs, flattened.</summary>
  private static readonly short[] _WhiteTable1 = [
    -1, -1, 12, -2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    11, 1792, 11, 1792, 12, 1984, 12, 2048, 12, 2112, 12, 2176, 12, 2240, 12, 2304,
    11, 1856, 11, 1856, 11, 1920, 11, 1920, 12, 2368, 12, 2432, 12, 2496, 12, 2560,
  ];

  /// <summary><c>whiteTable2</c>: 512 entries of (code length, value) pairs, flattened.</summary>
  private static readonly short[] _WhiteTable2 = [
    -1, -1, -1, -1, -1, -1, -1, -1, 8, 29, 8, 29, 8, 30, 8, 30,
    8, 45, 8, 45, 8, 46, 8, 46, 7, 22, 7, 22, 7, 22, 7, 22,
    7, 23, 7, 23, 7, 23, 7, 23, 8, 47, 8, 47, 8, 48, 8, 48,
    6, 13, 6, 13, 6, 13, 6, 13, 6, 13, 6, 13, 6, 13, 6, 13,
    7, 20, 7, 20, 7, 20, 7, 20, 8, 33, 8, 33, 8, 34, 8, 34,
    8, 35, 8, 35, 8, 36, 8, 36, 8, 37, 8, 37, 8, 38, 8, 38,
    7, 19, 7, 19, 7, 19, 7, 19, 8, 31, 8, 31, 8, 32, 8, 32,
    6, 1, 6, 1, 6, 1, 6, 1, 6, 1, 6, 1, 6, 1, 6, 1,
    6, 12, 6, 12, 6, 12, 6, 12, 6, 12, 6, 12, 6, 12, 6, 12,
    8, 53, 8, 53, 8, 54, 8, 54, 7, 26, 7, 26, 7, 26, 7, 26,
    8, 39, 8, 39, 8, 40, 8, 40, 8, 41, 8, 41, 8, 42, 8, 42,
    8, 43, 8, 43, 8, 44, 8, 44, 7, 21, 7, 21, 7, 21, 7, 21,
    7, 28, 7, 28, 7, 28, 7, 28, 8, 61, 8, 61, 8, 62, 8, 62,
    8, 63, 8, 63, 8, 0, 8, 0, 8, 320, 8, 320, 8, 384, 8, 384,
    5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 10,
    5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 10,
    5, 11, 5, 11, 5, 11, 5, 11, 5, 11, 5, 11, 5, 11, 5, 11,
    5, 11, 5, 11, 5, 11, 5, 11, 5, 11, 5, 11, 5, 11, 5, 11,
    7, 27, 7, 27, 7, 27, 7, 27, 8, 59, 8, 59, 8, 60, 8, 60,
    9, 1472, 9, 1536, 9, 1600, 9, 1728, 7, 18, 7, 18, 7, 18, 7, 18,
    7, 24, 7, 24, 7, 24, 7, 24, 8, 49, 8, 49, 8, 50, 8, 50,
    8, 51, 8, 51, 8, 52, 8, 52, 7, 25, 7, 25, 7, 25, 7, 25,
    8, 55, 8, 55, 8, 56, 8, 56, 8, 57, 8, 57, 8, 58, 8, 58,
    6, 192, 6, 192, 6, 192, 6, 192, 6, 192, 6, 192, 6, 192, 6, 192,
    6, 1664, 6, 1664, 6, 1664, 6, 1664, 6, 1664, 6, 1664, 6, 1664, 6, 1664,
    8, 448, 8, 448, 8, 512, 8, 512, 9, 704, 9, 768, 8, 640, 8, 640,
    8, 576, 8, 576, 9, 832, 9, 896, 9, 960, 9, 1024, 9, 1088, 9, 1152,
    9, 1216, 9, 1280, 9, 1344, 9, 1408, 7, 256, 7, 256, 7, 256, 7, 256,
    4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2,
    4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2,
    4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2,
    4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2,
    4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3,
    4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3,
    4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3,
    4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3,
    5, 128, 5, 128, 5, 128, 5, 128, 5, 128, 5, 128, 5, 128, 5, 128,
    5, 128, 5, 128, 5, 128, 5, 128, 5, 128, 5, 128, 5, 128, 5, 128,
    5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8,
    5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8,
    5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9,
    5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9,
    6, 16, 6, 16, 6, 16, 6, 16, 6, 16, 6, 16, 6, 16, 6, 16,
    6, 17, 6, 17, 6, 17, 6, 17, 6, 17, 6, 17, 6, 17, 6, 17,
    4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
    4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
    4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
    4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
    4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5,
    4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5,
    4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5,
    4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5,
    6, 14, 6, 14, 6, 14, 6, 14, 6, 14, 6, 14, 6, 14, 6, 14,
    6, 15, 6, 15, 6, 15, 6, 15, 6, 15, 6, 15, 6, 15, 6, 15,
    5, 64, 5, 64, 5, 64, 5, 64, 5, 64, 5, 64, 5, 64, 5, 64,
    5, 64, 5, 64, 5, 64, 5, 64, 5, 64, 5, 64, 5, 64, 5, 64,
    4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6,
    4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6,
    4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6,
    4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6, 4, 6,
    4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7,
    4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7,
    4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7,
    4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7, 4, 7,
  ];

  /// <summary><c>blackTable1</c>: 128 entries of (code length, value) pairs, flattened.</summary>
  private static readonly short[] _BlackTable1 = [
    -1, -1, -1, -1, 12, -2, 12, -2, -1, -1, -1, -1, -1, -1, -1, -1,
    -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    11, 1792, 11, 1792, 11, 1792, 11, 1792, 12, 1984, 12, 1984, 12, 2048, 12, 2048,
    12, 2112, 12, 2112, 12, 2176, 12, 2176, 12, 2240, 12, 2240, 12, 2304, 12, 2304,
    11, 1856, 11, 1856, 11, 1856, 11, 1856, 11, 1920, 11, 1920, 11, 1920, 11, 1920,
    12, 2368, 12, 2368, 12, 2432, 12, 2432, 12, 2496, 12, 2496, 12, 2560, 12, 2560,
    10, 18, 10, 18, 10, 18, 10, 18, 10, 18, 10, 18, 10, 18, 10, 18,
    12, 52, 12, 52, 13, 640, 13, 704, 13, 768, 13, 832, 12, 55, 12, 55,
    12, 56, 12, 56, 13, 1280, 13, 1344, 13, 1408, 13, 1472, 12, 59, 12, 59,
    12, 60, 12, 60, 13, 1536, 13, 1600, 11, 24, 11, 24, 11, 24, 11, 24,
    11, 25, 11, 25, 11, 25, 11, 25, 13, 1664, 13, 1728, 12, 320, 12, 320,
    12, 384, 12, 384, 12, 448, 12, 448, 13, 512, 13, 576, 12, 53, 12, 53,
    12, 54, 12, 54, 13, 896, 13, 960, 13, 1024, 13, 1088, 13, 1152, 13, 1216,
    10, 64, 10, 64, 10, 64, 10, 64, 10, 64, 10, 64, 10, 64, 10, 64,
  ];

  /// <summary><c>blackTable2</c>: 192 entries of (code length, value) pairs, flattened.</summary>
  private static readonly short[] _BlackTable2 = [
    8, 13, 8, 13, 8, 13, 8, 13, 8, 13, 8, 13, 8, 13, 8, 13,
    8, 13, 8, 13, 8, 13, 8, 13, 8, 13, 8, 13, 8, 13, 8, 13,
    11, 23, 11, 23, 12, 50, 12, 51, 12, 44, 12, 45, 12, 46, 12, 47,
    12, 57, 12, 58, 12, 61, 12, 256, 10, 16, 10, 16, 10, 16, 10, 16,
    10, 17, 10, 17, 10, 17, 10, 17, 12, 48, 12, 49, 12, 62, 12, 63,
    12, 30, 12, 31, 12, 32, 12, 33, 12, 40, 12, 41, 11, 22, 11, 22,
    8, 14, 8, 14, 8, 14, 8, 14, 8, 14, 8, 14, 8, 14, 8, 14,
    8, 14, 8, 14, 8, 14, 8, 14, 8, 14, 8, 14, 8, 14, 8, 14,
    7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10,
    7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10,
    7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10,
    7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10, 7, 10,
    7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11,
    7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11,
    7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11,
    7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11, 7, 11,
    9, 15, 9, 15, 9, 15, 9, 15, 9, 15, 9, 15, 9, 15, 9, 15,
    12, 128, 12, 192, 12, 26, 12, 27, 12, 28, 12, 29, 11, 19, 11, 19,
    11, 20, 11, 20, 12, 34, 12, 35, 12, 36, 12, 37, 12, 38, 12, 39,
    11, 21, 11, 21, 12, 42, 12, 43, 10, 0, 10, 0, 10, 0, 10, 0,
    7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12,
    7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12,
    7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12,
    7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12, 7, 12,
  ];

  /// <summary><c>blackTable3</c>: 64 entries of (code length, value) pairs, flattened.</summary>
  private static readonly short[] _BlackTable3 = [
    -1, -1, -1, -1, -1, -1, -1, -1, 6, 9, 6, 8, 5, 7, 5, 7,
    4, 6, 4, 6, 4, 6, 4, 6, 4, 5, 4, 5, 4, 5, 4, 5,
    3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1,
    3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4,
    2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
    2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
  ];

  #endregion
}
