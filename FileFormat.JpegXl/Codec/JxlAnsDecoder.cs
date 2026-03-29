using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// rANS (range Asymmetric Numeral Systems) entropy decoder.
/// JPEG XL uses rANS with distribution tables for entropy coding.
/// The decoder maintains a 32-bit state and reads bits from the codestream
/// to renormalize after each symbol decode.
/// </summary>
internal sealed class JxlAnsDecoder {

  private const uint _InitialState = 0x130000;
  private const int _StateBits = 32;
  private const int _RenormLowerBound = 1 << 16;

  private uint _state;
  private readonly JxlBitReader _reader;

  public JxlAnsDecoder(JxlBitReader reader) {
    _reader = reader ?? throw new ArgumentNullException(nameof(reader));
  }

  /// <summary>Initialize the rANS state by reading 32 bits from the bitstream.</summary>
  public void Init() => _state = _reader.ReadBits(_StateBits);

  /// <summary>
  /// Decode one symbol using the given distribution.
  /// Uses the alias table for O(1) lookup, then renormalizes the state.
  /// </summary>
  public int ReadSymbol(AnsDistribution dist) {
    ArgumentNullException.ThrowIfNull(dist);

    var tableSize = dist.TableSize;
    var logBucket = dist.LogBucketSize;
    var index = (int)(_state & (uint)(tableSize - 1));

    var symbol = dist.Symbols[index];
    var freq = dist.Frequencies[symbol];
    var cumFreq = dist.CumulativeFreqs[symbol];

    // rANS decode step: state = freq * (state >> logBucket) + (state & mask) - cumFreq
    _state = (uint)(freq * (_state >> logBucket) + (_state & (uint)(tableSize - 1)) - cumFreq);

    // Renormalize: read 16-bit chunks until state is large enough
    while (_state < _RenormLowerBound)
      _state = (_state << 16) | _reader.ReadBits(16);

    return symbol;
  }

  /// <summary>
  /// Verify the final rANS state matches the expected initial state.
  /// After decoding all symbols, the state should return to the initial value.
  /// </summary>
  public bool CheckFinalState() => _state == _InitialState;
}
