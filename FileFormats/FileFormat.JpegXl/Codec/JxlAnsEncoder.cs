using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// rANS (range Asymmetric Numeral Systems) entropy encoder.
/// Encodes symbols in reverse order to produce a bitstream that can be
/// decoded forward by <see cref="JxlAnsDecoder"/>.
/// </summary>
internal sealed class JxlAnsEncoder {

  private const uint _InitialState = 0x130000;
  private const int _RenormLowerBound = 1 << 16;

  private uint _state;
  private readonly List<ushort> _outputBits;

  public JxlAnsEncoder() {
    _state = _InitialState;
    _outputBits = new List<ushort>(256);
  }

  /// <summary>
  /// Encode a single symbol using the given distribution.
  /// Must be called in reverse order of the desired decode sequence.
  /// </summary>
  public void PutSymbol(AnsDistribution dist, int symbol) {
    ArgumentNullException.ThrowIfNull(dist);

    var freq = dist.Frequencies[symbol];
    if (freq <= 0)
      throw new InvalidOperationException($"Symbol {symbol} has zero frequency.");

    // Renormalize: output 16-bit chunks while state would overflow on encode.
    // Spec uses fixed AnsLogTabSize = 12 (table is always 4096 wide).
    var maxState = (uint)((long)freq * (_RenormLowerBound >> AnsDistribution.AnsLogTabSize) * (1 << 16) - 1);
    while (_state > maxState) {
      _outputBits.Add((ushort)(_state & 0xFFFF));
      _state >>= 16;
    }

    // rANS encode step. Decoder formula:
    //   res    = state mod 4096
    //   (sym, offset, freq) = AliasTable.Lookup(res)
    //   new_state = freq * (state >> 12) + offset
    //
    // Inverse (encoder): given new_state and chosen sym,
    //   q = new_state / freq[sym]
    //   o = new_state % freq[sym]                             // offset_within_freq
    //   r = encoder_slot[sym][o]                              // recovered residue
    //   old_state = (q << 12) | r                             // = q * 4096 + r
    //
    // The alias table is NOT in cumulative-frequency order, so we can't substitute
    // r = cumFreq[sym] + o. The reverse-slot table built by AnsDistribution.EncoderSlot
    // gives the exact inverse mapping per spec.
    var q = _state / (uint)freq;
    var o = (int)(_state % (uint)freq);
    var r = (uint)dist.EncoderSlot(symbol, o);
    _state = (q << AnsDistribution.AnsLogTabSize) | r;
  }

  /// <summary>
  /// Finalize the encoder and write the encoded data to the bit writer.
  /// Writes the final state as 32 bits, followed by the renormalization
  /// bits in reverse order (so decoding reads them forward).
  /// </summary>
  public void Finalize(JxlBitWriter writer) {
    ArgumentNullException.ThrowIfNull(writer);

    // Write final state
    writer.WriteBits(_state, 32);

    // Write renormalization bits in reverse (decoder reads them forward)
    for (var i = _outputBits.Count - 1; i >= 0; --i)
      writer.WriteBits(_outputBits[i], 16);
  }
}
