using System;
using System.IO;

namespace FileFormat.TextureEditorMikey;

/// <summary>Reads Atari 8-bit Texture Editor by Mikey (.txe) screens. from bytes, streams, or file paths.</summary>
public static class TextureEditorMikeyReader {

  public static TextureEditorMikeyFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TextureEditorMikeyFile FromStream(Stream stream) {
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

  public static TextureEditorMikeyFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != TextureEditorMikeyFile.FileSize)
      throw new InvalidDataException($"This format is exactly {TextureEditorMikeyFile.FileSize} bytes, got {data.Length}.");

    var header = new byte[TextureEditorMikeyFile.HeaderSize];
    data[..TextureEditorMikeyFile.HeaderSize].CopyTo(header);

    var screen = new byte[TextureEditorMikeyFile.ScreenDataSize];
    data.Slice(TextureEditorMikeyFile.HeaderSize, TextureEditorMikeyFile.ScreenDataSize).CopyTo(screen);

    return new() { Header = header, ScreenData = screen };
  }

  public static TextureEditorMikeyFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
