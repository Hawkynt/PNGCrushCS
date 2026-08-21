using System;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// The tables that say where a range coder state moves after each bit (RFC 9043 §3.8.1.4).
/// </summary>
/// <remarks>
/// The default table is published as 256 numbers and not as a formula, so it is transcribed here as
/// 256 numbers. A stream may state its own as differences from it, one signed number per state,
/// which is what a coder type of two means.
/// <para/>
/// Only one of the two directions is stored. The table that a one-bit follows is the one in the
/// file; the table a zero-bit follows is its mirror, <c>256 - one[256 - i]</c>, which is what makes
/// the two directions symmetrical about the middle of the range without a second table having to be
/// sent. The first eight entries and the last seven are zero because those states cannot be reached
/// from any valid stream.
/// </remarks>
internal static class Ffv1StateTransition {

  private static ReadOnlySpan<byte> _Default => [
      0,   0,   0,   0,   0,   0,   0,   0,  20,  21,  22,  23,  24,  25,  26,  27,
     28,  29,  30,  31,  32,  33,  34,  35,  36,  37,  37,  38,  39,  40,  41,  42,
     43,  44,  45,  46,  47,  48,  49,  50,  51,  52,  53,  54,  55,  56,  56,  57,
     58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,  71,  72,  73,
     74,  75,  75,  76,  77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,
     89,  90,  91,  92,  93,  94,  94,  95,  96,  97,  98,  99, 100, 101, 102, 103,
    104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 114, 115, 116, 117, 118,
    119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 133,
    134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149,
    150, 151, 152, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164,
    165, 166, 167, 168, 169, 170, 171, 171, 172, 173, 174, 175, 176, 177, 178, 179,
    180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 190, 191, 192, 194, 194,
    195, 196, 197, 198, 199, 200, 201, 202, 202, 204, 205, 206, 207, 208, 209, 209,
    210, 211, 212, 213, 215, 215, 216, 217, 218, 219, 220, 220, 222, 223, 224, 225,
    226, 227, 227, 229, 229, 230, 231, 232, 234, 234, 235, 236, 237, 238, 239, 240,
    241, 242, 243, 244, 245, 246, 247, 248, 248,   0,   0,   0,   0,   0,   0,   0,
  ];

  /// <summary>Builds both directions, with a stream's own differences applied where it states any.</summary>
  internal static (byte[] Zero, byte[] One) Build(ReadOnlySpan<int> deltas) {
    var one = new byte[256];
    for (var i = 0; i < 256; ++i)
      one[i] = (byte)((_Default[i] + (deltas.IsEmpty ? 0 : deltas[i])) & 0xFF);

    // The mirror. State zero would need a two hundred and fifty-sixth entry of the other table,
    // which does not exist; nothing reaches state zero, so it stays there.
    var zero = new byte[256];
    for (var i = 1; i < 256; ++i)
      zero[i] = (byte)((256 - one[256 - i]) & 0xFF);

    return (zero, one);
  }
}
