using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Lays out the DIF structure of a frame — every block's identifier and every control pack — so that
/// the coded picture has somewhere to go.
/// </summary>
/// <remarks>
/// A DIF sequence is always one header block, two subcode blocks and three VAUX blocks followed by
/// twenty-seven five-block video segments with nine audio blocks interleaved. SMPTE 370M changes the
/// number of channels and the video block layer, not that 150-block physical skeleton. Some 50-Hz
/// DV100 sequences contain no coded video; their DIF identifiers are still present and their payload
/// stays zero, matching the reference implementation.
/// <para/>
/// Converted from FFmpeg's LGPL-2.1-or-later <c>dv_format_frame</c>, <c>dv_write_pack</c>,
/// <c>dv_write_dif_id</c> and <c>dv_write_ssyb_id</c>; see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
internal static class DvFrameLayout {

  private const byte _SECTION_HEADER = 0x1f;
  private const byte _SECTION_SUBCODE = 0x3f;
  private const byte _SECTION_VAUX = 0x56;
  private const byte _SECTION_AUDIO = 0x76;
  private const byte _SECTION_VIDEO = 0x96;

  private const byte _PACK_HEADER_525 = 0x3f;
  private const byte _PACK_HEADER_625 = 0xbf;
  private const byte _PACK_VIDEO_SOURCE = 0x60;
  private const byte _PACK_VIDEO_CONTROL = 0x61;

  /// <summary>Writes the whole DIF skeleton of one frame, leaving video payloads for the segment encoder.</summary>
  internal static void Format(byte[] frame, DvProfile profile) {
    ArgumentNullException.ThrowIfNull(frame);
    ArgumentNullException.ThrowIfNull(profile);

    var at = 0;
    for (var channel = 0; channel < profile.ChannelCount; ++channel)
      for (var sequence = 0; sequence < profile.SequencesPerChannel; ++sequence) {
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
            at += 77;
          }

          at += _WriteBlockId(frame, at, _SECTION_VIDEO, channel, sequence, block);
          at += 77;
        }
      }
  }

  private static int _WriteBlockId(byte[] frame, int at, byte section, int channel, int sequence, int block) {
    frame[at] = section;
    frame[at + 1] = (byte)(
      (sequence << 4)
      | ((channel & 1) << 3)
      | ((1 - (channel >> 1)) << 2)
      | 3);
    frame[at + 2] = (byte)block;
    return 3;
  }

  private static int _WriteSubcodeId(byte[] frame, int at, int number, bool firstHalf) {
    frame[at] = (byte)((firstHalf ? 0x80 : 0x00) | 0x0f);
    frame[at + 1] = (byte)(0xf0 | (number & 0x0f));
    frame[at + 2] = 0xff;
    return 3;
  }

  private static int _WritePack(byte[] frame, int at, byte pack, DvProfile profile) {
    var application = profile.Sampling == DvSampling.FourTwoZero ? 0 : 1;

    frame[at] = pack;
    switch (pack) {
      case _PACK_HEADER_525:
      case _PACK_HEADER_625:
        frame[at + 1] = (byte)(0xf8 | application);
        frame[at + 2] = (byte)(0x78 | application);
        frame[at + 3] = (byte)(0x78 | application);
        frame[at + 4] = (byte)(0x78 | application);
        break;

      case _PACK_VIDEO_SOURCE:
        frame[at + 1] = 0xff;
        frame[at + 2] = 0xff;
        frame[at + 3] = (byte)(0xc0 | (profile.SequenceFlag << 5) | profile.SignalType);
        frame[at + 4] = 0xff;
        break;

      case _PACK_VIDEO_CONTROL:
        frame[at + 1] = 0x3f;
        frame[at + 2] = (byte)(0xc8 | (profile.IsDv100 ? 0x02 : 0x00));
        // FFmpeg's canonical writer emits 720p with FS=1 and 1080i with FS=0. Standard-definition
        // keeps the historical FS=1 value already used by this encoder. The interlace bit remains set
        // for all three because 720-line DV100 decoders key progressive handling from the profile.
        frame[at + 3] = profile.IsDv100 && profile.Height == 1080 ? (byte)0xbc : (byte)0xfc;
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
