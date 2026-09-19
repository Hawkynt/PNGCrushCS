using System;
using System.IO;

namespace FileFormat.Codecs.Roq;

/// <summary>
/// The persistent vector tables a <c>QUAD_CODEBOOK</c> chunk updates: up to 256 2x2 colour cells and
/// up to 256 4x4 cells built from four of them.
/// </summary>
/// <remarks>
/// A codebook chunk replaces the prefix it carries; entries above that prefix remain available. This
/// matters because the count bytes describe how many entries are transported by this chunk, not how
/// many entries exist after it. Both FFmpeg's LGPL decoder and the independently written Groovie RoQ
/// decoder keep their 256-entry tables and overwrite only the announced prefix.
/// <para/>
/// The Quake III form stores four luma bytes followed by Cb and Cr, six bytes per 2x2 cell. The older
/// Trilobyte form selected by <c>RoQ_INFO</c> argument 1 stores four Y/A pairs followed by Cb and Cr,
/// ten bytes per cell. The latter layout is factual bitstream data recovered from The 11th Hour and
/// Clandestiny samples; no implementation code is copied here.
/// </remarks>
internal sealed class RoqCodebook {

  private const int _STANDARD_CB2_ENTRY_LENGTH = 6;
  private const int _ALPHA_CB2_ENTRY_LENGTH = 10;
  private const int _CB4_ENTRY_LENGTH = 4;
  private const int _MAX_ENTRIES = 256;

  private byte[] _cb2 = new byte[_MAX_ENTRIES * _STANDARD_CB2_ENTRY_LENGTH];
  private readonly byte[] _cb4 = new byte[_MAX_ENTRIES * _CB4_ENTRY_LENGTH];

  internal bool HasAlpha { get; private set; }

  internal int Cb2Count { get; private set; }

  internal int Cb4Count { get; private set; }

  private int _Cb2EntryLength => this.HasAlpha ? _ALPHA_CB2_ENTRY_LENGTH : _STANDARD_CB2_ENTRY_LENGTH;

  /// <summary>Configures the cell representation selected by <c>RoQ_INFO</c>.</summary>
  internal void Configure(bool hasAlpha) {
    if (this.HasAlpha == hasAlpha)
      return;

    this.HasAlpha = hasAlpha;
    this._cb2 = new byte[_MAX_ENTRIES * this._Cb2EntryLength];
    Array.Clear(this._cb4);
    this.Cb2Count = 0;
    this.Cb4Count = 0;
  }

  /// <summary>The bytes of one 2x2 cell in the stream's selected six- or ten-byte representation.</summary>
  internal ReadOnlySpan<byte> Cb2(int index)
    => this._cb2.AsSpan(index * this._Cb2EntryLength, this._Cb2EntryLength);

  /// <summary>The four 2x2-cell indices — top left, top right, bottom left, bottom right — one 4x4
  /// cell is built from.</summary>
  internal ReadOnlySpan<byte> Cb4(int index) => this._cb4.AsSpan(index * _CB4_ENTRY_LENGTH, _CB4_ENTRY_LENGTH);

  /// <summary>
  /// Updates the prefixes transported by a <c>QUAD_CODEBOOK</c> chunk, preserving all older entries
  /// above them.
  /// </summary>
  /// <remarks>
  /// A zero count byte means 256 entries when the payload has room for them. The format has no second
  /// spelling for zero entries, so when exactly one table is omitted the payload length disambiguates
  /// zero from 256.
  /// </remarks>
  internal void Replace(ReadOnlySpan<byte> payload, ushort argument) {
    var rawCb2Count = (argument >> 8) & 0xFF;
    var rawCb4Count = argument & 0xFF;
    var cb2Count = rawCb2Count == 0 ? _MAX_ENTRIES : rawCb2Count;
    var cb4Count = rawCb4Count == 0 ? _MAX_ENTRIES : rawCb4Count;
    var cb2Length = this._Cb2EntryLength;
    var expected = cb2Count * cb2Length + cb4Count * _CB4_ENTRY_LENGTH;

    if (expected != payload.Length) {
      if (rawCb4Count == 0 && cb2Count * cb2Length == payload.Length)
        cb4Count = 0;
      else if (rawCb2Count == 0 && cb4Count * _CB4_ENTRY_LENGTH == payload.Length)
        cb2Count = 0;
      else
        throw new InvalidDataException(
          $"A RoQ_QUAD_CODEBOOK chunk is {payload.Length} bytes and its argument (0x{argument:X4}) implies "
          + $"{cb2Count} 2x2 cells and {cb4Count} 4x4 cells, {expected} bytes' worth in the "
          + $"{(this.HasAlpha ? "ten" : "six")}-byte cell layout. Neither reading of a zero count as 0 "
          + "instead of 256 reconciles the two.");
    }

    var cb2Bytes = cb2Count * cb2Length;
    payload[..cb2Bytes].CopyTo(this._cb2);
    payload.Slice(cb2Bytes, cb4Count * _CB4_ENTRY_LENGTH).CopyTo(this._cb4);

    this.Cb2Count = Math.Max(this.Cb2Count, cb2Count);
    this.Cb4Count = Math.Max(this.Cb4Count, cb4Count);

    for (var i = 0; i < cb4Count * _CB4_ENTRY_LENGTH; ++i) {
      var index = this._cb4[i];
      if (index >= this.Cb2Count)
        throw new InvalidDataException(
          $"A RoQ_QUAD_CODEBOOK chunk's 4x4 cell names 2x2 cell {index}, and only "
          + $"{this.Cb2Count} 2x2 entries have been defined so far.");
    }
  }
}
