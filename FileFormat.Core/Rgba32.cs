using System;

namespace FileFormat.Core;

/// <summary>One colour, eight bits a channel.</summary>
/// <remarks>
/// The framework's own colour type lives in <c>System.Drawing</c>, which pulls in a native GDI+
/// dependency that only exists on Windows. Code that merely needs to name a colour — a palette
/// entry, a pixel — should not have to carry that, so it names one with this instead.
/// <para/>
/// The channel order is the one a palette is written in: red first. That is deliberate, since every
/// palette in this tree is stored as RGB triplets and a struct that disagreed with them would put a
/// swap at each boundary, which is where colour-order mistakes come from.
/// </remarks>
public readonly record struct Rgba32(byte R, byte G, byte B, byte A) {

  /// <summary>A fully opaque colour.</summary>
  public Rgba32(byte red, byte green, byte blue) : this(red, green, blue, 255) { }

  /// <summary>Builds an opaque colour, clamping each channel to a byte.</summary>
  public static Rgba32 FromArgb(int red, int green, int blue)
    => new((byte)red, (byte)green, (byte)blue, 255);

  /// <summary>Builds a colour with an alpha, clamping each channel to a byte.</summary>
  public static Rgba32 FromArgb(int alpha, int red, int green, int blue)
    => new((byte)red, (byte)green, (byte)blue, (byte)alpha);

  /// <summary>The colour packed as 0xAARRGGBB, which is how a caller that wants one integer reads it.</summary>
  public int ToArgb() => (this.A << 24) | (this.R << 16) | (this.G << 8) | this.B;

  /// <summary>Opaque black.</summary>
  public static Rgba32 Black => new(0, 0, 0);

  /// <summary>Opaque white.</summary>
  public static Rgba32 White => new(255, 255, 255);

  /// <summary>Opaque red, which is the channel full on and the other two off.</summary>
  public static Rgba32 Red => new(255, 0, 0);

  /// <summary>Opaque green. Note this is the full channel, not the framework's darker named green.</summary>
  public static Rgba32 Green => new(0, 255, 0);

  /// <summary>Opaque blue.</summary>
  public static Rgba32 Blue => new(0, 0, 255);

  /// <summary>Nothing at all: every channel zero, including the alpha.</summary>
  public static Rgba32 Transparent => new(0, 0, 0, 0);

  public override string ToString() => $"#{this.A:X2}{this.R:X2}{this.G:X2}{this.B:X2}";
}
