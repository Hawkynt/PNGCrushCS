using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.LightWorkImage;

/// <summary>Assembles a LightWork Design texture: the records, the runs, then the closing records.</summary>
public static class LightWorkImageWriter {

  /// <summary>The longest run a count byte can hold.</summary>
  private const int _MaxRun = 255;

  public static byte[] ToBytes(LightWorkImageFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException("A LightWork image needs a width and a height.", nameof(file));

    var pixels = file.Pixels ?? [];
    if (pixels.Length != width * height * 3)
      throw new ArgumentException($"A LightWork picture of {width}x{height} needs {width * height * 3} bytes and has {pixels.Length}.", nameof(file));

    var result = new List<byte>(pixels.Length / 2 + 256);

    _WriteString(result, LightWorkImageFile.TagCopyright, LightWorkImageFile.Copyright, terminated: true);
    _WriteWord(result, 1);
    _WriteString(result, LightWorkImageFile.TagCreator, file.Creator ?? string.Empty, terminated: false);
    _WriteString(result, LightWorkImageFile.TagAuthor, file.Author ?? string.Empty, terminated: false);
    _WriteString(result, LightWorkImageFile.TagSource, file.Source ?? string.Empty, terminated: false);
    _WriteString(result, LightWorkImageFile.TagDate, file.Date ?? string.Empty, terminated: false);
    _WriteWord(result, LightWorkImageFile.TagPicture, 1);

    result.Add(LightWorkImageFile.TagSize);
    _WriteWord(result, width);
    _WriteWord(result, height);

    result.Add(LightWorkImageFile.TagWindow);
    _WriteWord(result, 0);
    _WriteWord(result, 0);
    _WriteWord(result, width);

    for (var at = 0; at < pixels.Length;) {
      var r = pixels[at];
      var g = pixels[at + 1];
      var b = pixels[at + 2];

      var run = 1;
      var next = at + 3;
      while (run < _MaxRun && next + 2 < pixels.Length && pixels[next] == r && pixels[next + 1] == g && pixels[next + 2] == b) {
        ++run;
        next += 3;
      }

      result.Add((byte)run);
      result.Add(r);
      result.Add(g);
      result.Add(b);
      at = next;
    }

    _WriteWord(result, 0);
    _WriteWord(result, LightWorkImageFile.TagPicture, 1);

    return result.ToArray();
  }

  private static void _WriteString(List<byte> target, byte tag, string value, bool terminated) {
    var bytes = Encoding.ASCII.GetBytes(value);
    var length = bytes.Length + (terminated ? 1 : 0);
    if (length > byte.MaxValue)
      throw new InvalidDataException($"A LightWork string record holds at most 255 bytes and this one is {length}.");

    target.Add(tag);
    target.Add((byte)length);
    target.AddRange(bytes);
    if (terminated)
      target.Add(0);
  }

  private static void _WriteWord(List<byte> target, byte tag, int value) {
    target.Add(tag);
    _WriteWord(target, value);
  }

  private static void _WriteWord(List<byte> target, int value) {
    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(word, value);
    target.Add(word[0]);
    target.Add(word[1]);
    target.Add(word[2]);
    target.Add(word[3]);
  }
}
