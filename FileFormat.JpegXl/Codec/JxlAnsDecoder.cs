using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// rANS (range Asymmetric Numeral Systems) entropy decoder. JPEG XL uses rANS with
/// a fixed-size alias table (<see cref="AnsDistribution.AnsTabSize"/> = 4096 slots,
/// <c>ANS_LOG_TAB_SIZE = 12</c>) per ISO/IEC 18181-1 §C.2 / libjxl's
/// <c>ReadSymbolANSWithoutRefill</c>.
/// </summary>
internal sealed class JxlAnsDecoder {

  /// <summary>Initial / final rANS state. <c>ANS_SIGNATURE = 0x13</c> in the high byte.</summary>
  private const uint _InitialState = 0x130000;

  /// <summary>State width: 32 bits (read at decoder init).</summary>
  private const int _StateBits = 32;

  /// <summary>Renormalization lower bound: when state drops below 2^16, refill 16 bits.</summary>
  private const int _RenormLowerBound = 1 << 16;

  /// <summary>Bits read per renormalization step.</summary>
  private const int _RenormBits = 16;

  private uint _state;
  private readonly JxlBitReader _reader;

  public JxlAnsDecoder(JxlBitReader reader) {
    _reader = reader ?? throw new ArgumentNullException(nameof(reader));
  }

  /// <summary>Initialize the rANS state by reading 32 bits LSB-first from the bitstream.</summary>
  public void Init() => _state = _reader.ReadBits(_StateBits);

  /// <summary>
  /// Decode one symbol using the given distribution. State update follows libjxl
  /// <c>dec_ans.h::ReadSymbolANSWithoutRefill</c>:
  /// <code>
  /// res    = state &amp; ANS_TAB_MASK
  /// sym    = AliasTable::Lookup(res, log_entry_size, entry_size-1)
  /// state  = sym.freq * (state &gt;&gt; ANS_LOG_TAB_SIZE) + sym.offset
  /// </code>
  /// then renormalize while state &lt; 2^16 by shifting in 16 fresh bits.
  /// </summary>
  public int ReadSymbol(AnsDistribution dist) {
    ArgumentNullException.ThrowIfNull(dist);
    ++SymbolsRead;

    // Spec: residue is always taken mod 4096 (the FIXED rANS table size),
    // independent of the per-distribution log_alpha_size.
    var res = (int)(_state & (uint)(AnsDistribution.AnsTabSize - 1));
    var (symbol, offset, freq) = dist.Lookup(res);

    // State update: state = freq * (state >> ANS_LOG_TAB_SIZE) + offset.
    _state = (uint)freq * (_state >> AnsDistribution.AnsLogTabSize) + (uint)offset;

    // Renormalize: when the state's high portion has drained, shift in 16 more bits.
    // Per libjxl `dec_ans.h::ReadSymbolANSWithoutRefill`, this is a SINGLE
    // refill (`if`, not `while`). One refill is sufficient because the rANS
    // state always grows past 2^16 after a single 16-bit refill (assuming
    // freq > 0). The libjxl `if` form also tolerates EOF: PeekFixedBits<16>
    // returns 0 past EOF, then Consume(16) just bumps the position counter
    // (validation is deferred to CheckANSFinalState).
    if (_state < _RenormLowerBound) {
      uint refillBits;
      if (_reader.HasBits(_RenormBits)) {
        refillBits = _reader.ReadBits(_RenormBits);
      } else {
        var available = (int)Math.Min(_RenormBits, _reader.BitsAvailable);
        refillBits = available > 0 ? _reader.ReadBits(available) : 0u;
      }
      _state = (_state << _RenormBits) | refillBits;
    }

    return symbol;
  }

  /// <summary>
  /// After all entropy-coded data has been read, the rANS state must equal
  /// <c>ANS_SIGNATURE &lt;&lt; 16</c> (= 0x130000). A spec-compliant decoder
  /// must call this at end-of-block to reject corrupted streams.
  /// </summary>
  public bool CheckFinalState() => _state == _InitialState;

  /// <summary>Diagnostic accessor: current rANS state (32-bit).
  /// Useful to print the exact value when CheckFinalState fails.</summary>
  internal uint State => _state;

  /// <summary>Diagnostic counter: number of ReadSymbol calls since Init.</summary>
  internal int SymbolsRead { get; private set; }
}
