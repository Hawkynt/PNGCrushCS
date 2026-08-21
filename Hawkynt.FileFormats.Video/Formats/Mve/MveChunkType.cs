namespace FileFormat.InterplayMve;

/// <summary>The two-byte chunk kinds an Interplay MVE file's chunk header names itself with.</summary>
internal static class MveChunkType {
  internal const ushort INIT_AUDIO = 0;
  internal const ushort AUDIO_ONLY = 1;
  internal const ushort INIT_VIDEO = 2;
  internal const ushort VIDEO = 3;
  internal const ushort SHUTDOWN = 4;
  internal const ushort END = 5;
}
