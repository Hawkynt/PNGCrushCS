using System;
using System.IO;
using System.Text;

namespace FileFormat.DaisyDotFont;

/// <summary>Reads Daisy-Dot fonts from bytes, streams, or file paths.</summary>
public static class DaisyDotFontReader {

  public static DaisyDotFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Font not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DaisyDotFontFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static DaisyDotFontFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 379
        || Encoding.ASCII.GetString(data[..DaisyDotFontFile.Signature.Length]) != DaisyDotFontFile.Signature
        || data[18] != DaisyDotFontFile.Terminator)
      throw new InvalidDataException("Not a Daisy-Dot font.");

    // There is no index, so the only way to check the characters is to walk them.
    var offset = DaisyDotFontFile.CharactersOffset;
    for (var i = 0; i < DaisyDotFontFile.CharacterCount; ++i) {
      if (offset >= data.Length)
        throw new InvalidDataException($"The font ends after {i} of {DaisyDotFontFile.CharacterCount} characters.");

      var width = data[offset];
      if (width == 0 || width > DaisyDotFontFile.MaxCharacterWidth)
        throw new InvalidDataException($"Character {i} is {width} columns wide.");

      var next = offset + (width + 1) * 2;
      if (next > data.Length || data[next - 1] != DaisyDotFontFile.Terminator)
        throw new InvalidDataException($"Character {i} is not closed where its width says it should be.");

      offset = next;
    }

    return new() { Data = data.ToArray() };
  }

  public static DaisyDotFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
