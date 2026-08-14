using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Miff;

/// <summary>Parses and formats MIFF text headers.</summary>
internal static class MiffHeaderParser {

  private const string _MAGIC = "id=ImageMagick";
  private const byte _TERMINATOR_BYTE = 0x1A;
  private const byte _COMMENT_OPEN = (byte)'{';
  private const byte _COMMENT_CLOSE = (byte)'}';

  /// <summary>Offset of the first header byte that is neither blank nor part of a comment.</summary>
  /// <remarks>
  /// The id line does not have to come first. A comment in braces may precede it, and XnView's
  /// nconvert writes one — <c>{\n  Created with XNview\n}\n</c> — ahead of everything else. Testing
  /// for the id at offset zero calls that file corrupt; ImageMagick, which owns the format, reads
  /// the comment and carries on.
  /// </remarks>
  public static int FindHeaderStart(ReadOnlySpan<byte> data) {
    var position = 0;
    while (position < data.Length) {
      var c = data[position];
      if (c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f') {
        ++position;
        continue;
      }

      if (c != _COMMENT_OPEN)
        break;

      position = _SkipComment(data, position);
    }

    return position;
  }

  /// <summary>Steps over one brace comment, including any nested inside it.</summary>
  /// <remarks>
  /// Returns the end of the data when the comment is never closed, which leaves the caller looking
  /// at nothing and refusing the file rather than reading the comment as fields.
  /// </remarks>
  private static int _SkipComment(ReadOnlySpan<byte> data, int position) {
    var depth = 0;
    while (position < data.Length) {
      var c = data[position++];
      switch (c) {
        case (byte)'\\':
          // A brace can be escaped so that it does not count towards the nesting.
          if (position < data.Length)
            ++position;
          break;
        case _COMMENT_OPEN:
          ++depth;
          break;
        case _COMMENT_CLOSE:
          if (--depth <= 0)
            return position;

          break;
      }
    }

    return position;
  }

  public static Dictionary<string, string> Parse(byte[] data, out int dataOffset) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var text = Encoding.ASCII.GetString(data);

    // Scanned a character at a time rather than a line at a time because a comment is delimited by
    // braces and not by line ends: it may open on one line and close on another, and anything
    // inside it — a colon above all — must not be read as structure.
    var line = new StringBuilder();
    var onlyBlankSoFar = true;
    var lastNonBlank = (byte)0;
    var position = 0;
    var terminator = -1;

    while (position < data.Length) {
      var c = data[position];

      if (c == _COMMENT_OPEN) {
        var end = _SkipComment(data, position);

        // A brace straight after '=' opens a value, not a comment: ImageMagick states its PNG chunk
        // notes as `png:bKGD={chunk was found ...}`, and treating those as comments drops fields the
        // writer put there. Only a brace starting a token is a comment.
        if (lastNonBlank == (byte)'=') {
          for (var i = position; i < end; ++i)
            line.Append(text[i]);

          onlyBlankSoFar = false;
          lastNonBlank = _COMMENT_CLOSE;
        }

        position = end;
        continue;
      }

      if (c is (byte)'\n' or (byte)'\r') {
        _ReadFields(line.ToString(), result);
        line.Clear();
        onlyBlankSoFar = true;
        lastNonBlank = 0;
        ++position;
        continue;
      }

      // The header ends at a colon, and the colon is not alone on its line: a writer emits a form
      // feed, a newline, the colon and then the control byte, with the samples following it
      // immediately. Looking for a line equal to ":" therefore reads the colon together with the
      // whole binary payload and never matches.
      if (c == (byte)':' && onlyBlankSoFar) {
        terminator = position;
        break;
      }

      if (c is not ((byte)' ' or (byte)'\t' or (byte)'\f')) {
        onlyBlankSoFar = false;
        lastNonBlank = c;
      }

      line.Append(text[position]);
      ++position;
    }

    if (terminator < 0)
      throw new InvalidDataException("MIFF header terminator ':' not found.");

    // What follows the colon varies: one writer ends the line first and another puts the control
    // byte straight after it, so both are stepped over — but only one of each. Skipping every
    // newline that follows eats a first sample that happens to be 0x0A, which shifts the whole
    // picture one channel to the left.
    var offset = terminator + 1;
    if (offset < data.Length && data[offset] == (byte)'\r')
      ++offset;

    if (offset < data.Length && data[offset] == (byte)'\n')
      ++offset;

    if (offset < data.Length && data[offset] == _TERMINATOR_BYTE)
      ++offset;

    dataOffset = offset;
    return result;
  }

  public static byte[] Format(MiffFile file) {
    var sb = new StringBuilder();
    sb.Append("id=ImageMagick\n");
    sb.Append("class=").Append(file.ColorClass == MiffColorClass.PseudoClass ? "PseudoClass" : "DirectClass").Append('\n');
    sb.Append("columns=").Append(file.Width).Append('\n');
    sb.Append("rows=").Append(file.Height).Append('\n');
    sb.Append("depth=").Append(file.Depth).Append('\n');
    sb.Append("type=").Append(file.Type).Append('\n');

    // How wide a sample packet is, in the field ImageMagick decides it by.
    //
    // It does not take that from `type`: its own files with an alpha channel carry no type line at
    // all, only these two. Saying TrueColorAlpha and nothing else therefore hands it four samples a
    // pixel to read three at a time, so every fourth byte becomes the next pixel's red and the
    // picture shears — 748 of 2257 pixels wrong on a 61x37 sample, while our own reader, which does
    // believe `type`, read the same file perfectly. Both fields are written because ImageMagick
    // writes both, and the older `matte` is what a reader predating alpha-trait looks for.
    var hasAlpha = file.Type != null
                   && (file.Type.Contains("Alpha", StringComparison.OrdinalIgnoreCase)
                       || file.Type.Contains("Matte", StringComparison.OrdinalIgnoreCase));

    sb.Append("alpha-trait=").Append(hasAlpha ? "Blend" : "Undefined").Append('\n');
    if (hasAlpha)
      sb.Append("matte=True\n");

    sb.Append("colorspace=").Append(file.Colorspace).Append('\n');

    if (file.Compression != MiffCompression.None)
      sb.Append("compression=").Append(file.Compression == MiffCompression.Rle ? "RLE" : "Zip").Append('\n');
    else
      sb.Append("compression=None\n");

    if (file.ColorClass == MiffColorClass.PseudoClass && file.Palette != null) {
      var colorCount = file.Palette.Length / 3;
      sb.Append("colors=").Append(colorCount).Append('\n');
    }

    // The samples begin at the byte after the control byte, and ImageMagick counts them from there
    // without looking: its reader takes the colon, discards exactly one byte and starts reading. A
    // newline between the colon and the control byte therefore costs it the control byte's place —
    // it reads the 0x1A as the first red sample and every sample after it lands one position late.
    // Our own reader steps over both and never noticed. This is the terminator ImageMagick writes.
    sb.Append("\f\n:");

    var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
    var result = new byte[headerBytes.Length + 1];
    Array.Copy(headerBytes, result, headerBytes.Length);
    result[headerBytes.Length] = _TERMINATOR_BYTE;

    return result;
  }

  /// <summary>Reads every key and value on one header line.</summary>
  /// <remarks>
  /// A line carries as many pairs as fit — <c>columns=13 rows=7 depth=16</c> is one line, not
  /// three — so a parser that splits on the first equals sign takes the rest of the line as one
  /// value and loses every field after the first.
  /// <para/>
  /// A value ordinarily runs to the next space, but one wrapped in braces may contain them, which
  /// is how a comment or a text chunk is carried.
  /// </remarks>
  private static void _ReadFields(string line, Dictionary<string, string> into) {
    var at = 0;
    while (at < line.Length) {
      while (at < line.Length && char.IsWhiteSpace(line[at]))
        ++at;

      var keyStart = at;
      while (at < line.Length && line[at] != '=' && !char.IsWhiteSpace(line[at]))
        ++at;

      if (at >= line.Length || line[at] != '=' || at == keyStart)
        return;

      var key = line[keyStart..at];
      ++at;

      string value;
      if (at < line.Length && line[at] == '{') {
        var close = line.IndexOf('}', at);
        if (close < 0)
          return;

        value = line[(at + 1)..close];
        at = close + 1;
      } else {
        var valueStart = at;
        while (at < line.Length && !char.IsWhiteSpace(line[at]))
          ++at;

        value = line[valueStart..at];
      }

      into[key] = value;
    }
  }
}
