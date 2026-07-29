using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Context-modeled integer encoder using direct bit coding for the simplified
/// modular encoding path. Writes tokens as fixed-width bit fields with no
/// entropy coding overhead, suitable for small images and simple data.
/// For full-spec encoding, this would use rANS with frequency-based distributions.
/// </summary>
internal sealed class JxlEntropyEncoder {

  private readonly JxlBitWriter _writer;
  private readonly int _numContexts;
  private readonly int _bitDepth;

  public JxlEntropyEncoder(JxlBitWriter writer, int numContexts, int bitDepth) {
    _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    _numContexts = numContexts;
    _bitDepth = bitDepth;
  }

  /// <summary>
  /// Write the entropy coding header for a simple direct-coded stream.
  /// Uses prefix codes with a flat distribution (each symbol = bitDepth bits).
  /// </summary>
  public void WriteHeader() {
    // LZ77 disabled
    _writer.WriteBool(false);

    // Use prefix codes (simpler than rANS for encoding)
    _writer.WriteBool(true);

    // Number of clusters = 1 (all contexts share one distribution)
    if (_numContexts > 1)
      _writer.WriteU32(1, 1, 0, 2, 0, 3, 0, 1, 6);

    // Hybrid integer config: split_exponent = 0 (direct coding)
    _writer.WriteU32(0, 0, 0, 4, 0, 8, 0, 0, 4);

    // Write a simple prefix code header for the alphabet
    var alphabetSize = (1 << _bitDepth) + 1; // +1 for possible overflow
    _writer.WriteU32((uint)(alphabetSize - 1), 2, 0, 4, 0, 8, 0, 1, 7);

    // All symbols have the same code length = bitDepth
    for (var i = 0; i < alphabetSize; ++i)
      _writer.WriteBits((uint)_bitDepth, 4);
  }

  /// <summary>
  /// Encode a single integer value for the given context.
  /// In the simplified encoder, this writes the value directly as a fixed-width field.
  /// </summary>
  public void WriteInt(int context, int value) {
    // For simplified encoding: write the unsigned value directly
    var unsigned = value < 0 ? (uint)(-value * 2 - 1) : (uint)(value * 2);
    if (_bitDepth > 0)
      _writer.WriteBits(unsigned, _bitDepth + 1);
    else
      _writer.WriteBits(unsigned, 1);
  }

  /// <summary>
  /// Write a raw signed integer using the zigzag encoding scheme.
  /// Maps signed values to unsigned: 0->0, -1->1, 1->2, -2->3, 2->4, etc.
  /// </summary>
  public void WriteSignedDirect(int value, int bits) {
    var unsigned = value >= 0 ? (uint)(value * 2) : (uint)(-value * 2 - 1);
    _writer.WriteBits(unsigned, bits);
  }

  /// <summary>
  /// Write a raw unsigned integer directly.
  /// </summary>
  public void WriteUnsignedDirect(uint value, int bits) => _writer.WriteBits(value, bits);
}
