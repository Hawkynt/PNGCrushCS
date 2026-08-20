using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Mpeg1.Tests;

/// <summary>
/// Writes MPEG-1 video elementary streams a bit at a time, so a test can state exactly which syntax
/// it is exercising.
/// </summary>
/// <remarks>
/// Every stream in this library's tests is built rather than checked in, and for a codec that matters
/// more than usual: the paths worth testing are the ones a real encoder never produces. ffmpeg's
/// MPEG-1 encoder emits no D pictures, no full-pel vectors, no macroblock stuffing and no intra
/// macroblocks inside B pictures, so comparing against it — which is how the decoder's arithmetic was
/// checked — cannot reach any of them. These can.
/// <para/>
/// Codes are given as the strings ISO/IEC 11172-2 Annex B prints them as, for the same reason the
/// decoder's tables are: a test that encodes with numbers is checking the decoder against a second
/// transcription of the same table, and two transcriptions of one table go wrong together.
/// </remarks>
internal sealed class Mpeg1TestStream {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal Mpeg1TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written the way the standard prints it; spaces are grouping.</summary>
  internal Mpeg1TestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Pads with zeroes to the next byte boundary, as every start code is preceded by.</summary>
  internal Mpeg1TestStream AlignToByte() {
    while (this._partialBits != 0)
      this._Bit(0);

    return this;
  }

  /// <summary>Appends a start code: <c>00 00 01</c> and the byte saying what follows.</summary>
  internal Mpeg1TestStream StartCode(byte code) {
    this.AlignToByte();
    this._bytes.Add(0x00);
    this._bytes.Add(0x00);
    this._bytes.Add(0x01);
    this._bytes.Add(code);
    return this;
  }

  /// <summary>A sequence header, optionally loading quantiser matrices.</summary>
  /// <param name="intraMatrix">Sixty-four weights in zig-zag scan order, or <c>null</c> for the default.</param>
  /// <param name="nonIntraMatrix">Likewise.</param>
  internal Mpeg1TestStream SequenceHeader(int width, int height, byte[]? intraMatrix = null, byte[]? nonIntraMatrix = null) {
    this.StartCode(0xB3);
    this.Bits(width, 12);
    this.Bits(height, 12);
    this.Bits(1, 4);       // pel_aspect_ratio: 1/1
    this.Bits(3, 4);       // picture_rate: 25
    this.Bits(0x3FFFF, 18);// bit_rate: unspecified
    this.Bits(1, 1);       // marker_bit
    this.Bits(0, 10);      // vbv_buffer_size
    this.Bits(0, 1);       // constrained_parameters_flag

    this.Bits(intraMatrix == null ? 0 : 1, 1);
    if (intraMatrix != null)
      foreach (var weight in intraMatrix)
        this.Bits(weight, 8);

    this.Bits(nonIntraMatrix == null ? 0 : 1, 1);
    if (nonIntraMatrix != null)
      foreach (var weight in nonIntraMatrix)
        this.Bits(weight, 8);

    return this;
  }

  /// <summary>A group-of-pictures header, whose twenty-seven bits change no sample.</summary>
  internal Mpeg1TestStream GroupOfPictures() {
    this.StartCode(0xB8);
    this.Bits(0, 25);  // time_code
    this.Bits(1, 1);   // closed_gop
    this.Bits(0, 1);   // broken_link
    return this;
  }

  /// <summary>A picture header.</summary>
  /// <param name="codingType">1 for I, 2 for P, 3 for B, 4 for D.</param>
  internal Mpeg1TestStream PictureHeader(
    int codingType, int temporalReference = 0,
    int forwardFCode = 1, bool forwardFullPel = false,
    int backwardFCode = 1, bool backwardFullPel = false) {
    this.StartCode(0x00);
    this.Bits(temporalReference, 10);
    this.Bits(codingType, 3);
    this.Bits(0xFFFF, 16); // vbv_delay

    if (codingType is 2 or 3) {
      this.Bits(forwardFullPel ? 1 : 0, 1);
      this.Bits(forwardFCode, 3);
    }

    if (codingType == 3) {
      this.Bits(backwardFullPel ? 1 : 0, 1);
      this.Bits(backwardFCode, 3);
    }

    this.Bits(0, 1); // extra_bit_picture
    return this;
  }

  /// <summary>A slice header. <paramref name="row"/> counts macroblock rows from zero.</summary>
  internal Mpeg1TestStream SliceHeader(int row, int quantiserScale) {
    this.StartCode((byte)(row + 1));
    this.Bits(quantiserScale, 5);
    this.Bits(0, 1); // extra_bit_slice
    return this;
  }

  /// <summary>The sequence end code, and the finished bytes.</summary>
  internal byte[] End() {
    this.StartCode(0xB7);
    return this.ToArray();
  }

  internal byte[] ToArray() {
    this.AlignToByte();
    return this._bytes.ToArray();
  }

  // --------------------------------------------------------------------------------------------
  // Block layer helpers
  // --------------------------------------------------------------------------------------------

  /// <summary>
  /// An intra block: a DC differential and then the run-level codes, ending in End of Block.
  /// </summary>
  /// <param name="isLuminance">Which of Table B.12 and Table B.13 sizes the differential.</param>
  /// <param name="differential">The DC difference from the previous intra block of this component.</param>
  /// <param name="coefficients">Codes for the alternating current coefficients, each a Table B.14
  /// code with its sign bit already appended.</param>
  internal Mpeg1TestStream IntraBlock(bool isLuminance, int differential, params string[] coefficients) {
    var size = _SizeOf(differential);
    this.Code(isLuminance ? _LuminanceDcSize(size) : _ChrominanceDcSize(size));

    if (size > 0)
      this.Bits(differential > 0 ? differential : differential + (1 << size) - 1, size);

    foreach (var code in coefficients)
      this.Code(code);

    return this.Code("10"); // End of Block
  }

  /// <summary>A non-intra block: run-level codes from the first-coefficient spelling, then End of Block.</summary>
  internal Mpeg1TestStream NonIntraBlock(params string[] coefficients) {
    foreach (var code in coefficients)
      this.Code(code);

    return this.Code("10");
  }

  /// <summary>How many bits ISO/IEC 11172-2 spends on a DC differential of this size.</summary>
  private static int _SizeOf(int differential) {
    var magnitude = Math.Abs(differential);
    var size = 0;
    while (magnitude >= 1 << size)
      ++size;

    return size;
  }

  /// <summary>Table B.12.</summary>
  private static string _LuminanceDcSize(int size) => size switch {
    0 => "100", 1 => "00", 2 => "01", 3 => "101", 4 => "110",
    5 => "1110", 6 => "1111 0", 7 => "1111 10", 8 => "1111 110",
    _ => throw new ArgumentOutOfRangeException(nameof(size)),
  };

  /// <summary>Table B.13.</summary>
  private static string _ChrominanceDcSize(int size) => size switch {
    0 => "00", 1 => "01", 2 => "10", 3 => "110", 4 => "1110",
    5 => "1111 0", 6 => "1111 10", 7 => "1111 110", 8 => "1111 1110",
    _ => throw new ArgumentOutOfRangeException(nameof(size)),
  };

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
