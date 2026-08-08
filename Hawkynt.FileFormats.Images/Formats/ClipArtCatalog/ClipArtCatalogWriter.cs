using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Wrappers;

namespace FileFormat.ClipArtCatalog;

/// <summary>Writes a clip-art catalogue: the chunks a browser walks to find its thumbnails.</summary>
/// <remarks>
/// The nesting is the samples' own. <c>CAT&#160;</c> states the length of everything after it,
/// <c>CLIP</c> says what kind of catalogue it is, and then one <c>FORM</c> per drawing holding a
/// <c>CLIPINFO</c> with the drawing's name, a <c>PATH</c>, and a <c>DIB&#160;</c> with the thumbnail.
/// <para/>
/// Every length is written from what actually follows it, and the chunks sit on even boundaries as
/// this family of formats has them do. That is what the reader accounts for the file by — the stated
/// length is the file's length less eight and the walk lands exactly on the end — so a catalogue this
/// writes is one it accepts by its own arithmetic rather than by luck.
/// <para/>
/// The drawings themselves are the files beside the catalogue and are not in it. What is written is
/// the index, with a thumbnail in it, which is what a catalogue is.
/// </remarks>
public static class ClipArtCatalogWriter {

  private static ReadOnlySpan<byte> _Form => "FORM"u8;
  private static ReadOnlySpan<byte> _ClipInfo => "CLIPINFO"u8;
  private static ReadOnlySpan<byte> _Path => "PATH"u8;
  private static ReadOnlySpan<byte> _Bitmap => "DIB "u8;

  public static byte[] ToBytes(ClipArtCatalogFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Entries.Count == 0)
      throw new ArgumentException("A clip-art catalogue holds thumbnails and this one has none.", nameof(file));

    using var body = new MemoryStream();
    body.Write(ClipArtCatalogFile.ClipTag);

    foreach (var entry in file.Entries) {
      var thumbnail = entry.Thumbnail ?? throw new ArgumentException("A clip-art entry carries no thumbnail.", nameof(file));
      if (thumbnail.Width < 1 || thumbnail.Height < 1
          || thumbnail.Width > ClipArtCatalogFile.MaxDimension || thumbnail.Height > ClipArtCatalogFile.MaxDimension)
        throw new ArgumentException(
          $"A clip-art thumbnail of {thumbnail.Width} by {thumbnail.Height} is outside the {ClipArtCatalogFile.MaxDimension} one holds.", nameof(file));

      var name = Encoding.ASCII.GetBytes(entry.Name ?? string.Empty);

      using var drawing = new MemoryStream();
      // The name chunk's tag is eight letters and the bitmap's is four; both are a tag then a length,
      // and the reader tells them apart by the second half rather than by a table of tags.
      _Chunk(drawing, _ClipInfo, [.. name, 0]);
      _Chunk(drawing, _Path, [0]);
      _Chunk(drawing, _Bitmap, WrappedDib.Encode(thumbnail));

      _Chunk(body, _Form, drawing.ToArray());
    }

    var payload = body.ToArray();
    var result = new byte[ClipArtCatalogFile.ChunkHeaderSize + payload.Length];
    ClipArtCatalogFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), payload.Length);
    payload.CopyTo(result, ClipArtCatalogFile.ChunkHeaderSize);

    return result;
  }

  /// <summary>A four-letter tag, a length, the body, and the pad that keeps the next chunk even.</summary>
  private static void _Chunk(Stream output, ReadOnlySpan<byte> tag, byte[] payload) {
    output.Write(tag);
    _Length(output, payload.Length);
    output.Write(payload);
    if ((payload.Length & 1) != 0)
      output.WriteByte(0);
  }

  private static void _Length(Stream output, int value) {
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, value);
    output.Write(length);
  }
}
