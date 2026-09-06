using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Lays out the DIF structure of a frame — every block's identifier and every control pack — so that
/// the coded picture has somewhere to go.
/// </summary>
/// <remarks>
/// A DV frame is a tape track written out as bytes, and the shape is the tape's rather than the
/// picture's. Each DIF sequence opens with one header block, two subcode blocks and three VAUX blocks;
/// then a hundred and thirty-five video blocks, with an audio block inserted before every fifteenth.
/// The audio blocks are written here with no audio in them — a video-only DV stream is what a file
/// carrying video alone looks like, and a player skips them on the strength of their own identifiers.
/// <para/>
/// Converted from FFmpeg's <c>dv_format_frame</c>, <c>dv_write_pack</c>, <c>dv_write_dif_id</c> and
/// <c>dv_write_ssyb_id</c> in <c>libavcodec/dvenc.c</c>.
/// <para/>
/// Two fields in the header pack are the only things a decoder actually needs from any of this: the
/// DIF sequence flag, which says 525/60 or 625/50, and the signal type in the VAUX source pack, which
/// says 25 or 50 Mbit and how the colour is sampled. Between them they name the profile. Everything
/// else here is written because a conforming file has it, not because anything reads it back.
/// </remarks>
internal static class DvFrameLayout {

  // DIF section identifiers.
  private const byte _SECTION_HEADER = 0x1f;
  private const byte _SECTION_SUBCODE = 0x3f;
  private const byte _SECTION_VAUX = 0x56;
  private const byte _SECTION_AUDIO = 0x76;
  private const byte _SECTION_VIDEO = 0x96;

  // Pack identifiers.
  private const byte _PACK_HEADER_525 = 0x3f;
  private const byte _PACK_HEADER_625 = 0xbf;
  private const byte _PACK_VIDEO_SOURCE = 0x60;
  private const byte _PACK_VIDEO_CONTROL = 0x61;

  /// <summary>Writes the whole DIF skeleton of one frame, leaving the video payloads empty.</summary>
  internal static void Format(byte[] frame, DvProfile profile) {
    ArgumentNullException.ThrowIfNull(frame);
    ArgumentNullException.ThrowIfNull(profile);

    var at = 0;

    for (var channel = 0; channel < profile.ChannelCount; ++channel)
      for (var sequence = 0; sequence < profile.SequencesPerChannel; ++sequence) {
        // The six control blocks that open a sequence are reserved-set throughout except where a pack
        // is written over them.
        Array.Fill(frame, (byte)0xff, at, DvProfile.DifBlockSize * 6);

        at += _WriteBlockId(frame, at, _SECTION_HEADER, channel, sequence, 0);
        at += _WritePack(frame, at, profile.SequenceFlag == 1 ? _PACK_HEADER_625 : _PACK_HEADER_525, profile);
        at += 72;

        for (var block = 0; block < 2; ++block) {
          at += _WriteBlockId(frame, at, _SECTION_SUBCODE, channel, sequence, block);
          for (var syb = 0; syb < 6; ++syb)
            at += _WriteSubcodeId(frame, at, syb, sequence < profile.SequencesPerChannel / 2) + 5;

          at += 29;
        }

        for (var block = 0; block < 3; ++block) {
          at += _WriteBlockId(frame, at, _SECTION_VAUX, channel, sequence, block);
          at += _WritePack(frame, at, _PACK_VIDEO_SOURCE, profile);
          at += _WritePack(frame, at, _PACK_VIDEO_CONTROL, profile);
          at += 7 * 5;
          at += _WritePack(frame, at, _PACK_VIDEO_SOURCE, profile);
          at += _WritePack(frame, at, _PACK_VIDEO_CONTROL, profile);
          at += 4 * 5 + 2;
        }

        for (var block = 0; block < 135; ++block) {
          if (block % 15 == 0) {
            Array.Fill(frame, (byte)0xff, at, DvProfile.DifBlockSize);
            at += _WriteBlockId(frame, at, _SECTION_AUDIO, channel, sequence, block / 15);
            at += 77; // audio control and shuffled samples, of which this stream has none
          }

          at += _WriteBlockId(frame, at, _SECTION_VIDEO, channel, sequence, block);
          at += 77; // one macroblock: a header byte, four luma blocks of 14 bytes and two of 10
        }
      }
  }

  /// <summary>Writes a DIF block's three-byte identifier.</summary>
  private static int _WriteBlockId(byte[] frame, int at, byte section, int channel, int sequence, int block) {
    frame[at] = section;
    frame[at + 1] = (byte)(
      (sequence << 4)              // 0-9 for 525/60, 0-11 for 625/50
      | ((channel & 1) << 3)       // FSC: which of the two channels at 50 Mbit
      | ((1 - (channel >> 1)) << 2) // FSP: which pair of channels at 100 Mbit
      | 3);                        // reserved — always set
    frame[at + 2] = (byte)block;
    return 3;
  }

  /// <summary>Writes a subcode sync block's three-byte identifier.</summary>
  /// <remarks>
  /// Six sync blocks to a subcode DIF block, so the numbers here only ever run 0 to 5 — where the
  /// application ID nibble is zero and the rest reserved, whichever of the six it is. The high bit
  /// says which half of the channel this sequence is in.
  /// </remarks>
  private static int _WriteSubcodeId(byte[] frame, int at, int number, bool firstHalf) {
    frame[at] = (byte)((firstHalf ? 0x80 : 0x00) | 0x0f);
    frame[at + 1] = (byte)(0xf0 | (number & 0x0f));
    frame[at + 2] = 0xff;
    return 3;
  }

  /// <summary>
  /// Writes one five-byte control pack.
  /// </summary>
  /// <remarks>
  /// The track application ID is the one field here with a real consequence. SMPTE 314M says it
  /// should be 001 when the signal came from a digital recorder and all ones when the source is
  /// unknown, but "unknown" is not a safe answer in practice: a 625/50 frame at 4:2:0 as IEC 61834
  /// defines it must carry 000, and the same frame at 4:1:1 as SMPTE 314M defines it must carry 001 —
  /// the two are the same size and the same signal type, and this field is the only thing that tells
  /// a decoder which of them it is holding.
  /// </remarks>
  private static int _WritePack(byte[] frame, int at, byte pack, DvProfile profile) {
    var application = profile.Sampling == DvSampling.FourTwoZero ? 0 : 1;

    frame[at] = pack;
    switch (pack) {
      case _PACK_HEADER_525:
      case _PACK_HEADER_625:
        frame[at + 1] = (byte)(0xf8 | application);        // APT: track application ID
        frame[at + 2] = (byte)(0x78 | application);        // TF1 clear — the audio is valid — and AP1
        frame[at + 3] = (byte)(0x78 | application);        // TF2 clear — the video is valid — and AP2
        frame[at + 4] = (byte)(0x78 | application);        // TF3 clear — the subcode is valid — and AP3
        break;

      case _PACK_VIDEO_SOURCE:
        frame[at + 1] = 0xff;
        frame[at + 2] = 0xff;                              // colour, colour-frame ID invalid, reserved
        frame[at + 3] = (byte)(0xc0 | (profile.SequenceFlag << 5) | profile.SignalType);
        frame[at + 4] = 0xff;                              // VISC: no information
        break;

      case _PACK_VIDEO_CONTROL:
        frame[at + 1] = 0x3f;                              // copy generation management: free
        frame[at + 2] = 0xc8;                              // reserved, and a 4:3 display aspect
        // Frame rather than field; second field first, matching what a progressive source is written
        // as; the picture differs from the one before; interlaced.
        frame[at + 3] = 0xfc;
        frame[at + 4] = 0xff;
        break;

      default:
        frame[at + 1] = 0xff;
        frame[at + 2] = 0xff;
        frame[at + 3] = 0xff;
        frame[at + 4] = 0xff;
        break;
    }

    return 5;
  }
}
