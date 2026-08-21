namespace FileFormat.InterplayMve;

/// <summary>
/// The one-byte opcode identifiers an Interplay MVE chunk's opcode stream names itself with, from
/// Mike Melanson's <c>interplay-mve.txt</c> and confirmed against every sample this was built against.
/// </summary>
internal static class MveOpcodeType {

  internal const byte END_OF_STREAM = 0x00;
  internal const byte END_OF_CHUNK = 0x01;
  internal const byte CREATE_TIMER = 0x02;
  internal const byte INIT_AUDIO_BUFFERS = 0x03;
  internal const byte START_STOP_AUDIO = 0x04;
  internal const byte INIT_VIDEO_BUFFERS = 0x05;
  internal const byte SEND_BUFFER = 0x07;
  internal const byte AUDIO_FRAME = 0x08;
  internal const byte AUDIO_SILENCE = 0x09;
  internal const byte INIT_VIDEO_MODE = 0x0A;
  internal const byte CREATE_GRADIENT = 0x0B;
  internal const byte SET_PALETTE = 0x0C;
  internal const byte SET_PALETTE_COMPRESSED = 0x0D;

  /// <summary>Packs one four-bit block encoding per 8x8 block into the picture's decoding map, read by
  /// <see cref="VIDEO_DATA"/>. Present in every video chunk that carries a new picture.</summary>
  internal const byte DECODING_MAP = 0x0F;

  /// <summary>The quadtree-coded picture: the only opcode that ever produces a frame.</summary>
  internal const byte VIDEO_DATA = 0x11;
}
