using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Flv;

/// <summary>What an AMF0 value turned out to be.</summary>
internal enum Amf0Kind {

  /// <summary>A double-precision number, which is the only number AMF0 has.</summary>
  Number,

  /// <summary>A boolean.</summary>
  Boolean,

  /// <summary>Text.</summary>
  String,

  /// <summary>An object or an ECMA array, both of which are named properties.</summary>
  Object,

  /// <summary>An array of values addressed by position.</summary>
  Array,

  /// <summary>An instant, which AMF0 writes as milliseconds since the Unix epoch.</summary>
  Date,

  /// <summary>The writer stated nothing.</summary>
  Null,
}

/// <summary>One named property of an AMF0 object or ECMA array.</summary>
internal readonly record struct Amf0Property(string Name, Amf0Value Value);

/// <summary>
/// One AMF0 value, as much of it as it carries.
/// </summary>
/// <remarks>
/// Nested values are kept rather than skipped over. An <c>onMetaData</c> may carry a <c>keyframes</c>
/// object of two parallel arrays and there is no telling in advance which of a writer's properties a
/// caller will want, so the parse keeps what it read; the alternative is a second parse for every
/// property that turns out to matter.
/// </remarks>
/// <param name="Kind">Which of the AMF0 types this is.</param>
/// <param name="Number">The value of a number, of a boolean as zero or one, or of a date in
/// milliseconds since the Unix epoch.</param>
/// <param name="Text">The value of a string.</param>
/// <param name="Properties">The named properties of an object or ECMA array.</param>
/// <param name="Elements">The values of an array, by position.</param>
internal sealed record Amf0Value(
  Amf0Kind Kind,
  double Number = 0d,
  string? Text = null,
  IReadOnlyList<Amf0Property>? Properties = null,
  IReadOnlyList<Amf0Value>? Elements = null) {

  /// <summary>The value of a named property, or <c>null</c> where this is not an object or has no such property.</summary>
  internal Amf0Value? this[string name] {
    get {
      if (this.Properties == null)
        return null;

      foreach (var property in this.Properties)
        if (property.Name == name)
          return property.Value;

      return null;
    }
  }
}

/// <summary>
/// Reads the AMF0 values an FLV's script tags are written as.
/// </summary>
/// <remarks>
/// AMF0 is Flash's own way of writing a value down, and an FLV's script tag is two of them: the name
/// of the message and its payload. It is not part of the container's framing — the tags are found
/// without reading a byte of this — which is why it lives beside the reader rather than inside it.
/// <para/>
/// Every read is bounds-checked and answers false rather than throwing, and the cursor is left where
/// the failure was found. A script tag is annotation: a file whose <c>onMetaData</c> is malformed is
/// still a file whose packets are all there, and refusing it for a broken comment would refuse a film
/// over its title. The framing is where refusal belongs, and that is <see cref="FlvTagScanner"/>'s.
/// </remarks>
internal static class Amf0Reader {

  private const byte _NUMBER = 0x00;
  private const byte _BOOLEAN = 0x01;
  private const byte _STRING = 0x02;
  private const byte _OBJECT = 0x03;
  private const byte _NULL = 0x05;
  private const byte _UNDEFINED = 0x06;
  private const byte _REFERENCE = 0x07;
  private const byte _ECMA_ARRAY = 0x08;
  private const byte _OBJECT_END = 0x09;
  private const byte _STRICT_ARRAY = 0x0A;
  private const byte _DATE = 0x0B;
  private const byte _LONG_STRING = 0x0C;
  private const byte _XML_DOCUMENT = 0x0F;
  private const byte _TYPED_OBJECT = 0x10;

  /// <summary>
  /// How deep a value may nest before the read is abandoned.
  /// </summary>
  /// <remarks>
  /// Nothing a writer emits comes close — <c>onMetaData</c>'s deepest is the <c>keyframes</c> object's
  /// arrays, which is three. The limit is here because the parse is recursive and the byte saying
  /// "another object starts here" is one byte, so a file of nothing but 0x03 would otherwise be a
  /// stack overflow rather than a refusal.
  /// </remarks>
  private const int _MAX_DEPTH = 32;

  /// <summary>Reads one value, leaving the cursor after it.</summary>
  internal static bool TryReadValue(ReadOnlySpan<byte> data, ref int at, out Amf0Value value)
    => _TryReadValue(data, ref at, 0, out value);

  private static bool _TryReadValue(ReadOnlySpan<byte> data, ref int at, int depth, out Amf0Value value) {
    value = new(Amf0Kind.Null);
    if (depth > _MAX_DEPTH || at >= data.Length)
      return false;

    var marker = data[at];
    ++at;

    switch (marker) {
      case _NUMBER: {
        if (!_TryReadDouble(data, ref at, out var number))
          return false;

        value = new(Amf0Kind.Number, number);
        return true;
      }

      case _BOOLEAN: {
        if (at >= data.Length)
          return false;

        value = new(Amf0Kind.Boolean, data[at] == 0 ? 0d : 1d);
        ++at;
        return true;
      }

      case _STRING: {
        if (!_TryReadShortString(data, ref at, out var text))
          return false;

        value = new(Amf0Kind.String, Text: text);
        return true;
      }

      // A long string and an XML document are the same bytes under two names: a 32-bit length and
      // that many bytes of UTF-8.
      case _LONG_STRING:
      case _XML_DOCUMENT: {
        if (at + 4 > data.Length)
          return false;

        var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        at += 4;
        if (length > (uint)(data.Length - at))
          return false;

        value = new(Amf0Kind.String, Text: _Utf8(data.Slice(at, (int)length)));
        at += (int)length;
        return true;
      }

      case _OBJECT:
        return _TryReadProperties(data, ref at, depth, out value);

      // An ECMA array states how many properties follow and then writes them exactly as an object
      // does, terminator and all. The count is the writer's claim and is not trusted to be right:
      // ffmpeg's own muxer writes the array's length there, and a file whose count disagrees with its
      // terminator is read by every player from the terminator.
      case _ECMA_ARRAY: {
        if (at + 4 > data.Length)
          return false;

        at += 4;
        return _TryReadProperties(data, ref at, depth, out value);
      }

      case _TYPED_OBJECT: {
        // The class name in front of the properties, which nothing here has a use for.
        if (!_TryReadShortString(data, ref at, out _))
          return false;

        return _TryReadProperties(data, ref at, depth, out value);
      }

      case _STRICT_ARRAY: {
        if (at + 4 > data.Length)
          return false;

        var declared = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        at += 4;

        // One byte is the shortest a value can be, so a count larger than the bytes left cannot be
        // honoured and the file is malformed. Clamping keeps the read inside the tag.
        var count = (int)Math.Min(declared, (uint)(data.Length - at));
        var elements = new Amf0Value[count];
        for (var i = 0; i < count; ++i)
          if (!_TryReadValue(data, ref at, depth + 1, out elements[i]))
            return false;

        value = new(Amf0Kind.Array, Elements: elements);
        return true;
      }

      case _DATE: {
        if (!_TryReadDouble(data, ref at, out var milliseconds) || at + 2 > data.Length)
          return false;

        // Two bytes of time zone follow, which the specification says are reserved and which every
        // writer leaves at zero. The instant itself is UTC.
        at += 2;
        value = new(Amf0Kind.Date, milliseconds);
        return true;
      }

      case _NULL:
      case _UNDEFINED:
        value = new(Amf0Kind.Null);
        return true;

      // A reference points at an object written earlier in the same message. Nothing an FLV writer
      // emits uses one, and resolving it would mean keeping every object read so far; the cursor is
      // still advanced correctly so whatever follows is read.
      case _REFERENCE: {
        if (at + 2 > data.Length)
          return false;

        at += 2;
        value = new(Amf0Kind.Null);
        return true;
      }

      // A marker with no length in it and no fixed size — a movie clip, a record set, the
      // "unsupported" marker. There is no way past it, so the read stops rather than resuming at a
      // byte that is not a marker.
      default:
        return false;
    }
  }

  /// <summary>Reads named properties up to the empty name and object-end marker that terminate them.</summary>
  private static bool _TryReadProperties(ReadOnlySpan<byte> data, ref int at, int depth, out Amf0Value value) {
    value = new(Amf0Kind.Null);

    var properties = new List<Amf0Property>();
    while (true) {
      if (!_TryReadShortString(data, ref at, out var name))
        return false;

      // The terminator is an empty name followed by the object-end marker, and it is the only place
      // in AMF0 where a name is not followed by a value.
      if (name.Length == 0) {
        if (at >= data.Length || data[at] != _OBJECT_END)
          return false;

        ++at;
        value = new(Amf0Kind.Object, Properties: properties);
        return true;
      }

      if (!_TryReadValue(data, ref at, depth + 1, out var property))
        return false;

      properties.Add(new(name, property));
    }
  }

  private static bool _TryReadShortString(ReadOnlySpan<byte> data, ref int at, out string text) {
    text = string.Empty;
    if (at + 2 > data.Length)
      return false;

    var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));
    at += 2;
    if (length > data.Length - at)
      return false;

    text = _Utf8(data.Slice(at, length));
    at += length;
    return true;
  }

  private static bool _TryReadDouble(ReadOnlySpan<byte> data, ref int at, out double value) {
    value = 0d;
    if (at + 8 > data.Length)
      return false;

    value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data.Slice(at, 8)));
    at += 8;
    return true;
  }

  private static string _Utf8(ReadOnlySpan<byte> data) => data.IsEmpty ? string.Empty : Encoding.UTF8.GetString(data);
}
