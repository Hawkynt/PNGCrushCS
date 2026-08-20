namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// The byte that follows <c>00 00 01</c> and says what the start code introduces
/// (ISO/IEC 11172-2, Table 2-B.1).
/// </summary>
/// <remarks>
/// The codec has its own copy of these rather than reaching for the container's. Start codes are
/// defined by the video standard and not by any container, and a decoder handed packets from an AVI,
/// a program stream or a caller's own buffer still has to find the headers inside them — so the
/// knowledge belongs to whichever side is doing the finding, and both sides are.
/// </remarks>
internal static class Mpeg1StartCode {

  /// <summary>picture_start_code: a coded picture's header follows.</summary>
  internal const byte Picture = 0x00;

  /// <summary>The lowest slice_start_code. The code's value is the slice's macroblock row, counted from one.</summary>
  internal const byte FirstSlice = 0x01;

  /// <summary>The highest slice_start_code, which is why a picture may hold at most 175 slices.</summary>
  internal const byte LastSlice = 0xAF;

  /// <summary>user_data_start_code: bytes with no meaning in the standard, up to the next start code.</summary>
  internal const byte UserData = 0xB2;

  /// <summary>sequence_header_code.</summary>
  internal const byte SequenceHeader = 0xB3;

  /// <summary>
  /// extension_start_code. Nothing in MPEG-1 is carried here; in MPEG-2 the sequence extension is,
  /// which is how the two are told apart.
  /// </summary>
  internal const byte Extension = 0xB5;

  /// <summary>sequence_end_code.</summary>
  internal const byte SequenceEnd = 0xB7;

  /// <summary>group_start_code: a group-of-pictures header follows.</summary>
  internal const byte Group = 0xB8;
}
