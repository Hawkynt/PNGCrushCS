using System;
using System.IO;

namespace FileFormat.Codecs.Roq;

/// <summary>
/// The vector tables a <c>QUAD_CODEBOOK</c> chunk states: up to 256 2x2 colour cells and up to 256
/// 4x4 cells, each of the latter built from four of the former.
/// </summary>
/// <remarks>
/// A codebook chunk is always a whole replacement, never a partial one the way Cinepak's are — every
/// chunk this was measured against carries exactly as many bytes as its own entry counts imply, with
/// nothing left over for a bitmap of which entries changed. So there is nothing here to carry forward
/// selectively; a new chunk simply replaces both tables outright, and a <c>QUAD_VQ</c> chunk that
/// arrives without one first keeps reading whichever tables the last <c>QUAD_CODEBOOK</c> left behind.
/// </remarks>
internal sealed class RoqCodebook {

  private const int _CB2_ENTRY_LENGTH = 6;
  private const int _CB4_ENTRY_LENGTH = 4;

  private byte[] _cb2 = [];
  private byte[] _cb4 = [];

  internal int Cb2Count { get; private set; }

  internal int Cb4Count { get; private set; }

  /// <summary>The four luma samples, Cb and Cr of one 2x2 cell, in that order.</summary>
  internal ReadOnlySpan<byte> Cb2(int index) => this._cb2.AsSpan(index * _CB2_ENTRY_LENGTH, _CB2_ENTRY_LENGTH);

  /// <summary>The four 2x2-cell indices — top left, top right, bottom left, bottom right — one 4x4
  /// cell is built from.</summary>
  internal ReadOnlySpan<byte> Cb4(int index) => this._cb4.AsSpan(index * _CB4_ENTRY_LENGTH, _CB4_ENTRY_LENGTH);

  /// <summary>
  /// Replaces both tables from a <c>QUAD_CODEBOOK</c> chunk's payload and argument.
  /// </summary>
  /// <remarks>
  /// The argument's two bytes are the two entry counts, and a byte of zero means 256 of that kind —
  /// except when the chunk's own stated length says otherwise. A chunk with room for its 2x2 cells and
  /// nothing else states zero 4x4 cells by leaving that byte zero too, which reads the same as "256"
  /// until the length is checked against it; the length is what tells the two apart, and is the only
  /// way to, since the format gives a zero byte no other spelling for "none of these."
  /// </remarks>
  internal void Replace(ReadOnlySpan<byte> payload, ushort argument) {
    var rawCb2Count = (argument >> 8) & 0xFF;
    var rawCb4Count = argument & 0xFF;
    var cb2Count = rawCb2Count == 0 ? 256 : rawCb2Count;
    var cb4Count = rawCb4Count == 0 ? 256 : rawCb4Count;
    var expected = cb2Count * _CB2_ENTRY_LENGTH + cb4Count * _CB4_ENTRY_LENGTH;

    if (expected != payload.Length) {
      if (rawCb4Count == 0 && cb2Count * _CB2_ENTRY_LENGTH == payload.Length)
        cb4Count = 0;
      else if (rawCb2Count == 0 && cb4Count * _CB4_ENTRY_LENGTH == payload.Length)
        cb2Count = 0;
      else
        throw new InvalidDataException(
          $"A RoQ_QUAD_CODEBOOK chunk is {payload.Length} bytes and its argument (0x{argument:X4}) implies "
          + $"{cb2Count} 2x2 cells and {cb4Count} 4x4 cells, {expected} bytes' worth. Neither reading of a "
          + "zero count as 0 instead of 256 reconciles the two.");
    }

    var cb2Bytes = cb2Count * _CB2_ENTRY_LENGTH;
    this._cb2 = payload[..cb2Bytes].ToArray();
    this._cb4 = payload.Slice(cb2Bytes, cb4Count * _CB4_ENTRY_LENGTH).ToArray();
    this.Cb2Count = cb2Count;
    this.Cb4Count = cb4Count;

    foreach (var index in this._cb4)
      if (index >= this.Cb2Count)
        throw new InvalidDataException(
          $"A RoQ_QUAD_CODEBOOK chunk's 4x4 cell names 2x2 cell {index}, and the chunk states only "
          + $"{this.Cb2Count} of those.");
  }
}
