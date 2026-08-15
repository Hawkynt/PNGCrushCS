namespace FileFormat.Core;

/// <summary>What a stream of a container carries.</summary>
/// <remarks>
/// A demuxer reports every stream, not only the pictures. The ones it cannot decode still have to be
/// counted and named: a stream's number is its position among all of them, so skipping the sound
/// would make the pictures go looking under the wrong one, and a muxer copying a file across has to
/// carry the sound through even when nothing here can decode it.
/// </remarks>
public enum MediaStreamKind {

  /// <summary>The container named a kind this library has no meaning for.</summary>
  Unknown,

  /// <summary>Pictures.</summary>
  Video,

  /// <summary>Sound.</summary>
  Audio,

  /// <summary>Subtitles or captions.</summary>
  Subtitle,

  /// <summary>Timed data that is neither picture, sound nor text — chapter marks, MIDI, telemetry.</summary>
  Data,
}
