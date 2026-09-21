namespace FileFormat.InterplayMve;

/// <summary>
/// The one-byte opcode identifiers an Interplay MVE chunk's opcode stream names itself with, from
/// Mike Melanson's <c>interplay-mve.txt</c> and confirmed against FFmpeg's demuxer/decoder.
/// </summary>
internal static class MveOpcodeType {

  internal const byte END_OF_STREAM = 0x00;
  internal const byte END_OF_CHUNK = 0x01;
  internal const byte CREATE_TIMER = 0x02;
  internal const byte INIT_AUDIO_BUFFERS = 0x03;
  internal const byte START_STOP_AUDIO = 0x04;
  internal const byte INIT_VIDEO_BUFFERS = 0x05;

  /// <summary>The oldest video-data form. Its 16-bit decoding map lives inside the payload.</summary>
  internal const byte VIDEO_DATA_06 = 0x06;

  internal const byte SEND_BUFFER = 0x07;
  internal const byte AUDIO_FRAME = 0x08;
  internal const byte AUDIO_SILENCE = 0x09;
  internal const byte INIT_VIDEO_MODE = 0x0A;
  internal const byte CREATE_GRADIENT = 0x0B;
  internal const byte SET_PALETTE = 0x0C;
  internal const byte SET_PALETTE_COMPRESSED = 0x0D;

  /// <summary>The skip/page map consumed by <see cref="VIDEO_DATA_10"/>.</summary>
  internal const byte SKIP_MAP = 0x0E;

  /// <summary>
  /// The external decoding map. Format 0x11 packs one four-bit block encoding per 8x8 block;
  /// format 0x10 consumes signed 16-bit entries only for blocks its skip map marks as changed.
  /// </summary>
  internal const byte DECODING_MAP = 0x0F;

  /// <summary>The three-stream page-update video form using skip map, decoding map and raw data.</summary>
  internal const byte VIDEO_DATA_10 = 0x10;

  /// <summary>The normal Interplay Video block-coded picture.</summary>
  internal const byte VIDEO_DATA_11 = 0x11;

  /// <summary>Compatibility name for the normal 0x11 video-data opcode.</summary>
  internal const byte VIDEO_DATA = VIDEO_DATA_11;
}
