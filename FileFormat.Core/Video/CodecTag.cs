using System;

namespace FileFormat.Core;

/// <summary>
/// The four-character code a container uses to name the codec its packets are coded with.
/// </summary>
/// <remarks>
/// Kept as the raw thirty-two bits rather than as a string because that is what the containers hold
/// and because not every code is four printable letters — <c>BI_RGB</c> is the number zero. The
/// value is the field as it sits in the file, little-endian, so <c>MJPG</c> is 0x47504A4D.
/// <para/>
/// This is deliberately not an enumeration of known codecs. A decoder says which tags it takes; a
/// container says which tag it found. Neither has to know the other's list, which is what lets a
/// codec be added without touching a container and the other way round.
/// </remarks>
/// <param name="Value">The four bytes of the code as one little-endian number.</param>
public readonly record struct CodecTag(uint Value) {

  /// <summary>The tag a container states when it has no code to give.</summary>
  public static readonly CodecTag None = new(0);

  /// <summary>Builds a tag from its four characters, in the order they appear in the file.</summary>
  public static CodecTag FromCharacters(string code) {
    ArgumentNullException.ThrowIfNull(code);
    if (code.Length != 4)
      throw new ArgumentException("A four-character code is exactly four characters long.", nameof(code));

    return new(code[0] | ((uint)code[1] << 8) | ((uint)code[2] << 16) | ((uint)code[3] << 24));
  }

  /// <summary>Whether this tag is the same code ignoring the case of its letters.</summary>
  /// <remarks>
  /// Spelling is not part of the identity: a container patched from <c>MJPG</c> to <c>mjpg</c> is
  /// read by ffprobe as the same codec with the same frame count, so a decoder that took only one
  /// spelling would refuse a file every other tool plays.
  /// </remarks>
  public bool EqualsIgnoringCase(CodecTag other) {
    for (var i = 0; i < 4; ++i) {
      var mine = (byte)(this.Value >> (i * 8));
      var theirs = (byte)(other.Value >> (i * 8));
      if (_ToUpper(mine) != _ToUpper(theirs))
        return false;
    }

    return true;

    static byte _ToUpper(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;
  }

  /// <summary>
  /// Renders the code the way a person would recognise it in an error message.
  /// </summary>
  /// <remarks>
  /// A refusal has to name the codec, and the number alone does not: nobody recognises 0x34363248,
  /// where everybody recognises H264. Codes that are not four printable characters — BI_RGB's zero
  /// among them — have no name to give, so those keep the number.
  /// </remarks>
  public override string ToString() {
    Span<char> letters = stackalloc char[4];
    for (var i = 0; i < 4; ++i) {
      var value = (byte)(this.Value >> (i * 8));
      if (value is < 0x20 or > 0x7E)
        return $"0x{this.Value:X8}";

      letters[i] = (char)value;
    }

    return new(letters);
  }
}
