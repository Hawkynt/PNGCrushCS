using System.IO;

namespace FileFormat.Eri;

/// <summary>Writes an ERI code stream most significant bit first.</summary>
internal sealed class EriBitWriter {

  private readonly MemoryStream _output = new();
  private int _byte;
  private int _bits;

  public void WriteBit(int bit) {
    this._byte = (this._byte << 1) | (bit & 1);
    if (++this._bits != 8)
      return;
    this._output.WriteByte((byte)this._byte);
    this._byte = 0;
    this._bits = 0;
  }

  public void WriteBits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this.WriteBit(value >> i);
  }

  /// <summary>Writes <paramref name="value"/> (at least 1) as the gamma code <see cref="EriBitReader.ReadGamma"/> reads.</summary>
  public void WriteGamma(int value) {
    if (value == 1) {
      this.WriteBit(0);
      return;
    }

    var top = 31 - int.LeadingZeroCount(value);
    this.WriteBit(1);
    for (var i = top - 1; i >= 0; --i) {
      this.WriteBit(value >> i);
      this.WriteBit(i > 0 ? 1 : 0);
    }
  }

  /// <summary>The bits written, the last byte padded with zeros.</summary>
  public byte[] ToArray() {
    if (this._bits > 0) {
      this._output.WriteByte((byte)(this._byte << (8 - this._bits)));
      this._byte = 0;
      this._bits = 0;
    }

    return this._output.ToArray();
  }

  /// <summary>How many bits a gamma code of <paramref name="value"/> takes.</summary>
  public static int GammaLength(int value) => value <= 1 ? 1 : 1 + 2 * (31 - int.LeadingZeroCount(value));
}
