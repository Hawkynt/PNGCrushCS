using System;
using System.IO;
using FileFormat.Wrappers;

namespace FileFormat.WizSolitaireDeck;

/// <summary>Reads a Wiz Solitaire deck from bytes, streams, or file paths.</summary>
public static class WizSolitaireDeckReader {

  public static WizSolitaireDeckFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WizSolitaireDeckFile FromStream(Stream stream) {
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

  public static WizSolitaireDeckFile FromSpan(ReadOnlySpan<byte> data) {
    var (embedded, isPng) = WrappedPicture.Extract(data, WizSolitaireDeckFile.Magic, "a Wiz Solitaire deck");
    return new() { Embedded = embedded, IsPng = isPng };
  }

  public static WizSolitaireDeckFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
