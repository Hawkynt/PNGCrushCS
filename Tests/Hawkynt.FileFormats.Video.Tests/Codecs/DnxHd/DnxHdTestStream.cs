using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.DnxHd.Tests;

/// <summary>
/// Writes VC-3 coding units a codeword at a time, so a test can state exactly which part of the
/// bitstream it is exercising.
/// </summary>
/// <remarks>
/// Built rather than checked in. The arithmetic of this decoder was measured against ffmpeg over
/// every compression identifier its encoder will write, at eight and ten bits, 4:2:2 and 4:4:4;
/// what these streams add is what that comparison cannot reach — the DC prediction exercised where
/// a natural picture would not go, the block order of Table 5, and the refusals, which by definition
/// no valid stream contains.
/// <para/>
/// A frame here is the smallest one the format allows: sixteen by sixteen samples, which is one
/// macroblock in one macroblock scan line. Every codeword is written out from the tables of Annex E
/// as the annex prints it, so a test states its bitstream in the standard's own notation rather than
/// through this decoder's idea of what the tables say.
/// </remarks>
internal sealed class DnxHdTestStream {

  /// <summary>The header size of an HD coding unit, and of every one ffmpeg writes, 7.2.</summary>
  private const int _HEADER_SIZE = 0x280;

  private const int _SCAN_INDICES_AT = 0x170;

  // Codewords of the tables compression ID 1274 selects, quoted from Annex E.
  //
  // Table E.6, the DC table: a codeword names η, the number of bits of the correction that follows.
  internal const string DcZeroBits = "000";   // η = 0
  internal const string DcFourBits = "010";   // η = 4

  // Table E.4, the amplitude table.
  internal const string EndOfBlock = "101";                // the end-of-block codeword
  internal const string AmplitudeOne = "00";               // amplitude 1, no run, no index
  internal const string AmplitudeSixteen = "111110101";    // amplitude 16, no run, no index
  internal const string AmplitudeSixteenWithRun = "1111111011110"; // amplitude 16, with a run

  // Table E.5, the zero-run table.
  internal const string RunOne = "0";

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  // ============================================================================================
  // The compressed payload
  // ============================================================================================

  /// <summary>Appends a codeword written as its bits, the way Annex E prints one.</summary>
  internal DnxHdTestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal DnxHdTestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>The twelve-bit macroblock header of 7.3.1.1: eleven bits of scale factor, then a flag.</summary>
  internal DnxHdTestStream Macroblock(int quantisationScale, int lowBit = 0)
    => this.Bits(quantisationScale, 11).Bits(lowBit, 1);

  /// <summary>A block holding nothing but a DC correction of zero.</summary>
  internal DnxHdTestStream FlatBlock() => this.Code(DcZeroBits).Code(EndOfBlock);

  /// <summary>
  /// A block whose DC correction is the four-bit value <paramref name="rho"/> and which has no AC.
  /// </summary>
  /// <remarks>
  /// 8.2.4 biases the correction: a ρ in the top half of its range stands for itself and one in the
  /// bottom half for a negative value, so 8 through 15 are +8 through +15 and 0 through 7 are −16
  /// through −9.
  /// </remarks>
  internal DnxHdTestStream DcBlock(int rho) => this.Code(DcFourBits).Bits(rho, 4).Code(EndOfBlock);

  /// <summary>Pads with zero bits to the next four-byte boundary, as 7.3.1 has a scan line end.</summary>
  internal DnxHdTestStream EndScanLine() {
    while (this._partialBits != 0)
      this._Bit(0);

    while (this._bytes.Count % 4 != 0)
      this._bytes.Add(0);

    return this;
  }

  /// <summary>The byte offset the next scan line will start at.</summary>
  internal int Position => this._bytes.Count;

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  // ============================================================================================
  // The coding unit around it
  // ============================================================================================

  /// <summary>Everything a test may want to say differently about a coding unit header.</summary>
  internal sealed class Options {
    internal int Width { get; init; } = 16;
    internal int Height { get; init; } = 16;
    internal int CompressionId { get; init; } = 1274;
    internal int HeaderVersion { get; init; } = 3;
    internal int BitDepthCode { get; init; } = 1;
    internal int SubSampling { get; init; }
    internal bool Interlaced { get; init; }
    internal bool FrameEncoded { get; init; } = true;
    internal bool AdaptiveMacroblocks { get; init; }
    internal bool Alpha { get; init; }
    internal bool Rgb { get; init; }
    internal int ColorVolume { get; init; }

    /// <summary>Overrides the stated header size, for the tests that want it wrong.</summary>
    internal int? StatedHeaderSize { get; init; }

    /// <summary>The byte offset of each macroblock scan line; one entry for a single-row frame.</summary>
    internal int[] ScanIndices { get; init; } = [0];
  }

  /// <summary>Assembles one coding unit: a 640-byte header and the payload after it.</summary>
  internal static byte[] Unit(Options options, DnxHdTestStream payload) {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(payload);

    payload.EndScanLine();

    var header = new byte[_HEADER_SIZE];
    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(options.StatedHeaderSize ?? _HEADER_SIZE));
    header[4] = (byte)options.HeaderVersion;

    // 7.2.2, Figure 19. The fixed bits are the ones the figure prints as constants.
    header[5] = 0x01;
    header[6] = (byte)(0x80 | (options.AdaptiveMacroblocks ? 0x20 : 0));
    header[7] = (byte)(0xA0 | (options.Alpha ? 0x01 : 0));

    // 7.2.3, Figure 20.
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x18), (ushort)options.Height);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x1A), (ushort)options.Width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x1D), (ushort)options.Height);
    header[0x21] = (byte)((options.BitDepthCode << 5) | 0x18);
    header[0x22] = (byte)(0x88 | (options.Interlaced ? 0x04 : 0));

    // 7.2.4 and 7.2.5.
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x28), (uint)options.CompressionId);
    header[0x2C] = (byte)((options.FrameEncoded ? 0x80 : 0)
                          | (options.SubSampling << 5)
                          | (options.ColorVolume << 1)
                          | (options.Rgb ? 1 : 0));

    // 7.2.10 and 7.2.11.
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x16C), (ushort)options.ScanIndices.Length);
    for (var i = 0; i < options.ScanIndices.Length; ++i)
      BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(_SCAN_INDICES_AT + i * 4), (uint)options.ScanIndices[i]);

    var unit = new List<byte>(header);
    unit.AddRange(payload._bytes);

    // A few spare bytes, because a decoder reading the last codeword of the last block may look
    // ahead of it and a real coding unit is padded to a stated size anyway (7.3.2).
    unit.AddRange(new byte[16]);

    return unit.ToArray();
  }

  /// <summary>A stream description naming DNxHD at the size the coding units will state.</summary>
  internal static MediaStreamInfo Stream(int width = 16, int height = 16, string codec = "AVdn") => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
  };
}
