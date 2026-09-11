using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>Writes the fields VP3.1 places in front of a picture.</summary>
internal static class Vp3FrameWriter {
  internal static void WriteIntraHeader(Vp3BitWriter writer, int quantisationIndex) {
    if ((uint)quantisationIndex >= 64)
      throw new ArgumentOutOfRangeException(nameof(quantisationIndex));

    writer.WriteBit(0); // intra frame
    writer.WriteBit(0); // unused
    writer.WriteBits((uint)quantisationIndex, 6);
    writer.WriteBits(0, 4); // width code; the container carries the actual width
    writer.WriteBits(0, 4); // height code
    writer.WriteBits(1, 5); // VP3.1 bitstream version
    writer.WriteBit(0); // normal key-frame coding type
    writer.WriteBits(0, 2); // reserved
  }

  /// <summary>
  /// Writes the front of an inter frame, which is the same two flags and quantisation index without
  /// the sixteen bits of geometry and version an intra frame carries.
  /// </summary>
  /// <remarks>
  /// Those sixteen bits are not optional on an inter frame -- they are simply not there. A decoder
  /// reads the frame-type bit first and only then knows how much header to expect, which is why an
  /// inter frame cannot begin a stream and why writing the intra header on one would put every field
  /// after it sixteen bits out.
  /// </remarks>
  internal static void WriteInterHeader(Vp3BitWriter writer, int quantisationIndex) {
    if ((uint)quantisationIndex >= 64)
      throw new ArgumentOutOfRangeException(nameof(quantisationIndex));

    writer.WriteBit(1); // inter frame
    writer.WriteBit(0); // unused
    writer.WriteBits((uint)quantisationIndex, 6);
  }
}
