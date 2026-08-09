using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Vrml;

/// <summary>Pulls the inline bitmap out of a VRML 2.0 scene.</summary>
/// <remarks>
/// The scene is read as text and only three things are wanted from it: the header that says which
/// language it is, a <c>PixelTexture</c> node, and that node's <c>image</c> field. Everything else —
/// the shape wearing the texture, its material, its transform — describes geometry and is passed
/// over, because geometry is not a picture.
/// <para/>
/// The field's own grammar decides where it ends: three numbers, then one number a pixel, then the
/// closing brace. Anything else standing where a number should be ends the field, and the count is
/// then required to be exactly right — a field short by one pixel is a truncated file, not a smaller
/// picture, and is refused rather than padded.
/// </remarks>
public static class VrmlReader {

  /// <summary>Most components a pixel may have: grey, grey and alpha, colour, colour and alpha.</summary>
  private const int _MAX_COMPONENTS = 4;

  /// <summary>The node carrying a bitmap inside the scene.</summary>
  private const string _NODE = "PixelTexture";

  /// <summary>That node's field holding the bitmap itself.</summary>
  private const string _FIELD = "image";

  public static VrmlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Scene not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VrmlFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static VrmlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static VrmlFile FromSpan(ReadOnlySpan<byte> data) {
    // Latin-1 rather than UTF-8: the header names UTF-8 but every token this reader cares about is
    // ASCII, and a scene carrying a stray byte in a string or a comment should not stop the picture
    // in it from being read.
    var text = Encoding.Latin1.GetString(_WithoutByteOrderMark(data));
    if (!text.StartsWith(VrmlFile.Header, StringComparison.Ordinal))
      throw new InvalidDataException($"A VRML 2.0 scene opens with \"{VrmlFile.Header}\"; this one does not.");

    var tokens = _Tokenize(text);
    var at = _IndexOfToken(tokens, _NODE, 0);
    if (at < 0)
      throw new InvalidDataException("The scene carries no PixelTexture, so there is no picture in it.");

    at = _IndexOfToken(tokens, _FIELD, at + 1);
    if (at < 0)
      throw new InvalidDataException("The PixelTexture states no image field.");

    if (!_TryNumber(tokens, at + 1, out var width)
        || !_TryNumber(tokens, at + 2, out var height)
        || !_TryNumber(tokens, at + 3, out var components))
      throw new InvalidDataException("An image field opens with a width, a height and a component count.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A picture is at least one pixel each way; this one states {width} by {height}.");
    if (components is < 1 or > _MAX_COMPONENTS)
      throw new InvalidDataException($"A pixel has one to four components; this field states {components}.");

    var count = (long)width * height;
    if (count > int.MaxValue / _MAX_COMPONENTS)
      throw new InvalidDataException($"A picture of {width} by {height} does not fit in memory.");

    var pixels = new byte[count * components];
    var first = at + 4;
    for (var i = 0L; i < count; ++i) {
      if (!_TryNumber(tokens, first + (int)i, out var value))
        throw new InvalidDataException(
          $"The image field states {width} by {height} and carries {i} pixels, which is not that many.");

      // Bottom row first: a texture's origin is its lower-left corner, so what the field lists
      // first is the last row of the picture.
      var x = i % width;
      var y = height - 1 - i / width;
      var to = (y * width + x) * components;
      for (var c = 0; c < components; ++c)
        pixels[to + c] = (byte)((uint)value >> ((components - 1 - c) * 8));
    }

    if (_TryNumber(tokens, first + (int)count, out _))
      throw new InvalidDataException(
        $"The image field states {width} by {height} and carries more pixels than that.");

    return new() { Width = width, Height = height, Components = components, PixelData = pixels };
  }

  private static ReadOnlySpan<byte> _WithoutByteOrderMark(ReadOnlySpan<byte> data)
    => data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? data[3..] : data;

  /// <summary>
  /// Splits the scene into words, dropping comments and treating the punctuation as words of its own.
  /// </summary>
  /// <remarks>
  /// A <c>#</c> runs to the end of the line and a comma counts as blank space — both are what the
  /// language says, and both matter: the header itself is a comment, and a producer is free to
  /// separate the pixels with commas rather than spaces.
  /// </remarks>
  private static List<string> _Tokenize(string text) {
    var tokens = new List<string>();
    var word = new StringBuilder();

    for (var i = 0; i < text.Length; ++i) {
      var c = text[i];
      if (c == '#') {
        _Flush(tokens, word);
        while (i < text.Length && text[i] != '\n')
          ++i;
        continue;
      }

      if (c is '"') {
        _Flush(tokens, word);
        for (++i; i < text.Length && text[i] != '"'; ++i)
          if (text[i] == '\\')
            ++i;
        continue;
      }

      if (char.IsWhiteSpace(c) || c == ',') {
        _Flush(tokens, word);
        continue;
      }

      if (c is '{' or '}' or '[' or ']') {
        _Flush(tokens, word);
        tokens.Add(c.ToString());
        continue;
      }

      word.Append(c);
    }

    _Flush(tokens, word);
    return tokens;
  }

  private static void _Flush(List<string> tokens, StringBuilder word) {
    if (word.Length <= 0)
      return;

    tokens.Add(word.ToString());
    word.Clear();
  }

  private static int _IndexOfToken(List<string> tokens, string what, int from) {
    for (var i = from; i < tokens.Count; ++i)
      if (string.Equals(tokens[i], what, StringComparison.Ordinal))
        return i;

    return -1;
  }

  /// <summary>Reads one of the field's numbers, which the language writes in decimal or in hex.</summary>
  private static bool _TryNumber(List<string> tokens, int at, out int value) {
    value = 0;
    if (at < 0 || at >= tokens.Count)
      return false;

    var token = tokens[at];
    if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
      if (!uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        return false;

      value = unchecked((int)hex);
      return true;
    }

    if (!uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var plain))
      return false;

    value = unchecked((int)plain);
    return true;
  }
}
