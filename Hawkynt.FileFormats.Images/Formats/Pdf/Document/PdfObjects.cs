using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Pdf.Document;

/// <summary>A PDF dictionary: names to objects, references left unresolved until asked for.</summary>
/// <remarks>
/// Names are plain strings and strings are byte arrays throughout the document layer, which is the
/// distinction the file itself makes: a name is an identifier, a string is bytes whose meaning
/// depends on where it is used.
/// </remarks>
internal sealed class PdfDict : Dictionary<string, object?> {

  public PdfDict() : base(StringComparer.Ordinal) { }

  /// <summary>The value under a key, unresolved, or null when absent.</summary>
  public object? Raw(string key) => this.TryGetValue(key, out var value) ? value : null;
}

/// <summary>A stream object: its dictionary and its bytes as they sit in the file, already decrypted.</summary>
internal sealed class PdfStreamObject(PdfDict dictionary, byte[] data) {

  /// <summary>The stream dictionary.</summary>
  public PdfDict Dictionary { get; } = dictionary;

  /// <summary>The encoded bytes, with any encryption removed but every filter still applied.</summary>
  public byte[] Data { get; internal set; } = data;

  /// <summary>The object this stream was read as, which is what its decryption key is derived from.</summary>
  public int ObjectNumber { get; init; }

  /// <summary>The generation of that object.</summary>
  public int Generation { get; init; }

  /// <summary>The decoded bytes, cached because content streams and fonts are asked for more than once.</summary>
  internal byte[]? Decoded { get; set; }
}

/// <summary>An operator in a content stream, kept apart from names so the interpreter can tell them apart.</summary>
internal readonly record struct PdfOperator(string Name) {
  public override string ToString() => this.Name;
}

/// <summary>Small helpers for reading values out of the object model.</summary>
internal static class PdfValues {

  /// <summary>The number an object holds, or a default.</summary>
  public static double Number(object? value, double fallback = 0) => value switch {
    int i => i,
    double d => d,
    long l => l,
    _ => fallback,
  };

  /// <summary>Whether an object is a number at all.</summary>
  public static bool IsNumber(object? value) => value is int or double or long;

  /// <summary>The integer an object holds, rounding a real, or a default.</summary>
  public static int Integer(object? value, int fallback = 0) => value switch {
    int i => i,
    double d when double.IsFinite(d) && Math.Abs(d) < int.MaxValue => (int)Math.Round(d),
    long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
    _ => fallback,
  };

  /// <summary>A string as Latin-1 text, which is what names stored as strings and most keys are.</summary>
  public static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

  /// <summary>
  /// A text string as text: UTF-16BE when it opens with a byte-order mark, UTF-8 when it opens with
  /// one, and PDFDocEncoding (taken as Latin-1, which agrees for every printable character) otherwise.
  /// </summary>
  public static string Text(byte[] bytes) {
    if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
      return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
      return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

    return Latin1(bytes);
  }
}
