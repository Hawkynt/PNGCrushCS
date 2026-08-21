using System.IO;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// The handful of fields at the front of a VP3 frame: whether it is an intra frame, and the one
/// quantisation index everything in it is coded at.
/// </summary>
/// <remarks>
/// <b>This is the part of the format the Theora specification does not state.</b> Section 7.1 of that
/// specification describes Theora's frame header and says only that VP3's "is substantially
/// different", with "a larger number of unused, reserved bits" and room for a single quantisation
/// index rather than three. The layout below was therefore derived from VP3 streams rather than read
/// off a page, and this is how:
/// <list type="number">
/// <item>The first bit is the frame type, because it is zero on the first packet of every stream —
/// which must be an intra frame — and one on the packets that follow.</item>
/// <item>The number of bits before the coded-block flags begin was found by decoding whole frames at
/// each candidate length and keeping the one that worked. That is a sharp test rather than a
/// plausible one: a frame's tokens are read until every coded block has all sixty-four of its
/// coefficient positions accounted for and no end-of-block run is left open, and a frame read from
/// the wrong bit position fails that within a few hundred tokens. Exactly one length passed, and it
/// left between two and fifteen bits of the packet unread — which is the padding to the next byte
/// that Section 5.2.4 says a packet ends with. Intra frames carry sixteen bits more than inter
/// frames.</item>
/// <item>Which six of the first eight bits hold the quantisation index was settled by decoding whole
/// frames both ways and comparing the result against a reference decoder sample by sample. The two
/// readings differ by a factor in every dequantised coefficient, so one of them is wrong everywhere;
/// bits two to seven is the one that is right everywhere.</item>
/// </list>
/// The bit between the frame type and the quantisation index, and the sixteen an intra frame carries
/// after them, are not read. Both VP3.1 files this was derived from state the same sixteen bits on
/// every intra frame — a one in the fifth-from-last position and zeroes elsewhere — which is
/// consistent with the frame size and bitstream version being restated there, but nothing in these
/// streams varies those fields, so nothing here claims to know what they are. What the container says
/// the frame size is, is what it is; a stream whose header meant something else would fail the
/// tokens-account-for-everything test above rather than decode into a wrong picture.
/// </remarks>
internal readonly struct Vp3FrameHeader {

  /// <summary>The bits before the coded-block flags of an inter frame.</summary>
  private const int _INTER_HEADER_BITS = 8;

  /// <summary>How many more bits an intra frame carries.</summary>
  private const int _INTRA_HEADER_EXTRA_BITS = 16;

  /// <summary><c>true</c> for an intra frame, which is coded without reference to any other.</summary>
  internal bool IsIntra { get; init; }

  /// <summary>The quantisation index every block of the frame is dequantised at.</summary>
  internal int QuantisationIndex { get; init; }

  internal static Vp3FrameHeader Read(Vp3BitReader reader) {
    var isIntra = reader.ReadBit() == 0;
    reader.ReadBit();
    var quantisationIndex = reader.ReadBits(6);

    if (isIntra)
      reader.ReadBits(_INTRA_HEADER_EXTRA_BITS);

    return new() { IsIntra = isIntra, QuantisationIndex = quantisationIndex };
  }

  /// <summary>How many bits a frame of the given type spends on its header.</summary>
  internal static int BitsFor(bool isIntra) =>
    _INTER_HEADER_BITS + (isIntra ? _INTRA_HEADER_EXTRA_BITS : 0);

  internal static void RequireIntraFirst(bool isIntra) {
    if (!isIntra)
      throw new InvalidDataException(
        "This VP3 stream begins with an inter frame, which is coded as differences from a frame that "
        + "was never sent. Decoding can only start at an intra frame.");
  }
}
