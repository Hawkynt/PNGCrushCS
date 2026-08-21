namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The start codes of ISO/IEC 14496-2, Table 6-3: the byte that follows <c>00 00 01</c>.
/// </summary>
internal static class Mpeg4StartCode {

  /// <summary>The lowest video object start code; the low five bits are the object's number.</summary>
  internal const byte FirstVideoObject = 0x00;

  /// <summary>The highest video object start code.</summary>
  internal const byte LastVideoObject = 0x1F;

  /// <summary>The lowest video object layer start code; the low four bits are the layer's number.</summary>
  internal const byte FirstVideoObjectLayer = 0x20;

  /// <summary>The highest video object layer start code.</summary>
  internal const byte LastVideoObjectLayer = 0x2F;

  /// <summary>Visual object sequence: the outermost wrapper, carrying the profile and level.</summary>
  internal const byte VisualObjectSequence = 0xB0;

  /// <summary>The end of a visual object sequence.</summary>
  internal const byte VisualObjectSequenceEnd = 0xB1;

  /// <summary>User data, which the standard gives no meaning to.</summary>
  internal const byte UserData = 0xB2;

  /// <summary>A group of video object planes.</summary>
  internal const byte GroupOfVideoObjectPlanes = 0xB3;

  /// <summary>A marker a transmission error puts in; not something an encoder writes.</summary>
  internal const byte VideoSessionError = 0xB4;

  /// <summary>Visual object: what kind of thing the layers below describe.</summary>
  internal const byte VisualObject = 0xB5;

  /// <summary>One coded picture.</summary>
  internal const byte VideoObjectPlane = 0xB6;

  /// <summary>The first of the codes ISO/IEC 14496-2 reserves.</summary>
  internal const byte FirstReserved = 0xB7;
}
