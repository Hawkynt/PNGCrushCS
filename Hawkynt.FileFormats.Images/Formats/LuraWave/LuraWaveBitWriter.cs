using System.Buffers.Binary;
using System.IO;

namespace FileFormat.LuraWave;

/// <summary>
/// Packs bits most significant first into big-endian 32-bit words, escaping any data word that would read as a control
/// word (<c>FFFFFFxx</c>) — the counterpart of <see cref="LuraWaveBitReader"/>.
/// </summary>
internal sealed class LuraWaveBitWriter {

  private const uint ControlPrefix = 0xFFFFFF00;
  private const uint Escape = 0xFFFFFF00;
  private const uint StripeEnd = 0xFFFFFFFF;

  private readonly MemoryStream _output = new();
  private uint _word;
  private int _filled;

  /// <summary>Once the traversal has spent its pass budget the decoder stops listening, and so does the writer.</summary>
  public LuraWaveReadState State { get; set; }

  public void Put(bool bit) {
    this._word = this._word << 1 | (bit ? 1u : 0u);
    if (++this._filled == 32)
      this.FlushWord();
  }

  /// <summary>Pads the last word with zero bits and closes the stripe with its terminator.</summary>
  public byte[] Finish() {
    if (this._filled > 0) {
      this._word <<= 32 - this._filled;
      this.FlushWord();
    }

    this.WriteWord(StripeEnd);
    return this._output.ToArray();
  }

  private void FlushWord() {
    if ((this._word & ControlPrefix) == ControlPrefix)
      this.WriteWord(Escape);
    this.WriteWord(this._word);
    this._word = 0;
    this._filled = 0;
  }

  private void WriteWord(uint word) {
    System.Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, word);
    this._output.Write(bytes);
  }
}
