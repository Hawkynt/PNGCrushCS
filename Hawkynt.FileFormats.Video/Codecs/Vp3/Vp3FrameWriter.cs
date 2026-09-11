using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>Writes the fields VP3.1 places in front of an intra picture.</summary>
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
}
