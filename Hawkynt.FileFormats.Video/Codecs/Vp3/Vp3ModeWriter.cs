using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Writes an inter frame's macro block coding modes and motion vectors, in the form
/// <c>Vp3ModeReader</c> reads them.
/// </summary>
/// <remarks>
/// Both fields are written in their literal form: coding scheme seven spends three bits per macro
/// block instead of a Huffman code, and the motion vector flag selects a fixed five-bits-and-a-sign
/// component instead of the variable-length one. Neither is a fallback the format merely tolerates --
/// both are ordinary VP3, chosen by a bit the decoder reads before either field -- and a writer that
/// used the Huffman forms would have to carry a second transcription of tables that exist here only
/// to be read. The cost is a few bits per macro block on a codec whose predicted frames are mostly
/// macro blocks that say nothing at all.
/// <para/>
/// A macro block whose four luminance blocks are all uncoded carries no mode: the reader takes its
/// silence as mode zero and moves on, so writing one would put every macro block after it a codeword
/// out of place.
/// </remarks>
internal static class Vp3ModeWriter {

  /// <summary>The scheme that spends three bits per macro block rather than a Huffman code.</summary>
  private const int _SCHEME_LITERAL = 7;

  /// <summary>Inter, predicted from the previous frame with no displacement.</summary>
  internal const int INTER_NO_MOTION = 0;

  /// <summary>Intra, predicted from nothing.</summary>
  internal const int INTRA = 1;

  /// <summary>Inter, predicted from the previous frame at a stated vector.</summary>
  internal const int INTER_MOTION = 2;

  internal static void WriteModes(Vp3BitWriter writer, Vp3Geometry geometry, bool[] coded, byte[] modes) {
    writer.WriteBits(_SCHEME_LITERAL, 3);

    for (var macroblock = 0; macroblock < geometry.MacroblockCount; ++macroblock) {
      if (!_CarriesAMode(geometry, coded, macroblock))
        continue;

      writer.WriteBits((uint)modes[macroblock], 3);
    }
  }

  /// <summary>Writes one vector for every macro block whose mode states it carries one.</summary>
  /// <remarks>
  /// The flag that chooses the component form is written whether or not anything in the frame needs a
  /// vector, because the reader reads it unconditionally.
  /// </remarks>
  internal static void WriteMotionVectors(
    Vp3BitWriter writer, Vp3Geometry geometry, bool[] coded, byte[] modes, int[] vectorX, int[] vectorY) {
    writer.WriteBit(1); // literal components

    for (var macroblock = 0; macroblock < geometry.MacroblockCount; ++macroblock) {
      if (!_CarriesAMode(geometry, coded, macroblock) || modes[macroblock] != INTER_MOTION)
        continue;

      _WriteComponent(writer, vectorX[macroblock]);
      _WriteComponent(writer, vectorY[macroblock]);
    }
  }

  /// <summary>Whether a macro block states a mode at all, which is whether any of it is coded.</summary>
  internal static bool _CarriesAMode(Vp3Geometry geometry, bool[] coded, int macroblock) {
    var luma = geometry.MacroblockLumaBlocks[macroblock];
    return coded[luma[0]] || coded[luma[1]] || coded[luma[2]] || coded[luma[3]];
  }

  /// <summary>
  /// Writes one vector component as five bits of magnitude and a sign.
  /// </summary>
  /// <remarks>
  /// Five bits gives a magnitude of nought to thirty-one, and the sign is separate rather than part
  /// of a two's-complement field -- so negative thirty-one is stateable and negative thirty-two is
  /// not, which is not what a signed five-bit field would give.
  /// </remarks>
  private static void _WriteComponent(Vp3BitWriter writer, int component) {
    var magnitude = component < 0 ? -component : component;
    if (magnitude > 31)
      throw new ArgumentOutOfRangeException(
        nameof(component), component, "A VP3 motion vector component states a magnitude of at most thirty-one.");

    writer.WriteBits((uint)magnitude, 5);
    writer.WriteBit(component < 0 ? 1 : 0);
  }
}
