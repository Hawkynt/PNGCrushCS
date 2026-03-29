using System;
using System.Text;

namespace FileFormat.Riff;

/// <summary>A four-character code used to identify RIFF chunks and forms.</summary>
public readonly record struct FourCC(byte A, byte B, byte C, byte D) {

  public FourCC(string value) : this(
    value is not { Length: 4 } ? throw new ArgumentException("FourCC must be exactly 4 characters.", nameof(value)) : (byte)value[0],
    (byte)value[1],
    (byte)value[2],
    (byte)value[3]
  ) { }

  public static implicit operator FourCC(string value) => new(value);
  public static implicit operator string(FourCC fourCC) => fourCC.ToString();

  public override string ToString() => Encoding.ASCII.GetString([this.A, this.B, this.C, this.D]);

  public void WriteTo(Span<byte> destination) {
    destination[0] = this.A;
    destination[1] = this.B;
    destination[2] = this.C;
    destination[3] = this.D;
  }

  public static FourCC ReadFrom(ReadOnlySpan<byte> source) => new(source[0], source[1], source[2], source[3]);
}
