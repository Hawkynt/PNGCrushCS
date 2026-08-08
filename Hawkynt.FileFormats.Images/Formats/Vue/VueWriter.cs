using System;
using System.IO;
using System.Text;

namespace FileFormat.Vue;

/// <summary>Writes a Vue d'Esprit object file: the header, the two strings, the size, then the GIF.</summary>
/// <remarks>
/// The header is the thirty bytes both samples carry — the program's name, a nul, and the version
/// line — and the two strings go out in the order the file keeps them, the description first and the
/// name after it, each behind its own two-byte length. Following those lengths has to land exactly on
/// the picture's own signature, which is the check the reader makes and what these lengths are
/// written from.
/// </remarks>
public static class VueWriter {

  /// <summary>The name, a nul, and the version line that follows it.</summary>
  private const int _HeaderSize = 30;

  /// <summary>What the samples state after the name.</summary>
  private const string _Version = " Version 2.0  vob";

  /// <summary>Longer than any string either sample carries, and what the reader refuses past.</summary>
  private const int _LargestString = 4096;

  public static byte[] ToBytes(VueFile file) {
    var embedded = file.Embedded ?? throw new ArgumentException("A Vue object carries a picture and this one has none.", nameof(file));
    if (embedded.Length < 6 || embedded[0] != 'G' || embedded[1] != 'I' || embedded[2] != 'F' || embedded[3] != '8')
      throw new ArgumentException("A Vue object carries its picture as a GIF and this one does not begin as one.", nameof(file));

    if (file.Width < 1 || file.Height < 1)
      throw new ArgumentException($"A Vue object of {file.Width} by {file.Height} states no picture.", nameof(file));

    var description = _Text(file.Description, nameof(file.Description));
    var name = _Text(file.Name, nameof(file.Name));

    using var output = new MemoryStream();
    var header = new byte[_HeaderSize];
    VueFile.Magic.CopyTo(header);
    Encoding.Latin1.GetBytes(_Version).CopyTo(header, VueFile.Magic.Length);
    output.Write(header);

    _String(output, description);
    _String(output, name);
    _UInt32(output, file.Width);
    _UInt32(output, file.Height);
    output.Write(embedded);

    return output.ToArray();
  }

  private static byte[] _Text(string? value, string what) {
    var text = Encoding.Latin1.GetBytes(value ?? string.Empty);
    if (text.Length > _LargestString)
      throw new ArgumentException($"A Vue object's {what} of {text.Length} bytes is longer than the {_LargestString} one holds.", what);

    return text;
  }

  private static void _String(Stream output, byte[] text) {
    output.WriteByte((byte)text.Length);
    output.WriteByte((byte)(text.Length >> 8));
    output.Write(text);
  }

  private static void _UInt32(Stream output, int value) {
    output.WriteByte((byte)value);
    output.WriteByte((byte)(value >> 8));
    output.WriteByte((byte)(value >> 16));
    output.WriteByte((byte)(value >> 24));
  }
}
