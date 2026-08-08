using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.PhotoParade;

/// <summary>Writes a PhotoParade album: the header, then each photograph followed by the block describing it.</summary>
/// <remarks>
/// The order is what makes the file readable. A description block stands immediately after the
/// photograph it describes, and that is how one is found: the picture is the run whose own markers
/// run out exactly where the next block begins. Writing a block before its picture, or leaving a gap
/// between them, would produce a file in which no photograph could be located at all.
/// <para/>
/// A chunk is a four-letter tag, a big-endian length and that many bytes, and a block ends with a
/// chunk tagged <c>fini</c>. The album block at the end states <c>NUMP</c>, the number of
/// photographs, which the reader checks against the number it found — a disagreement there is what
/// says something was misread — so it is written from the album itself.
/// <para/>
/// What is not written is the theme. A real file ends with a compressed listing of the theme's own
/// members in a scheme nothing here has worked out, and the backdrop, the border tile and the preview
/// that come before the first photograph belong to it. None of that is invented; what goes out is the
/// album.
/// </remarks>
public static class PhotoParadeWriter {

  /// <summary>The version the header states between its two signatures.</summary>
  private static ReadOnlySpan<byte> _Version => [0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00];

  /// <summary>The chunks a photograph's description block carries.</summary>
  private static ReadOnlySpan<byte> _PathTag => "PATH"u8;
  private static ReadOnlySpan<byte> _TitleTag => "TITL"u8;
  private static ReadOnlySpan<byte> _MemberTag => "MEMB"u8;

  public static byte[] ToBytes(PhotoParadeFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Photographs.Count == 0)
      throw new ArgumentException("A PhotoParade album holds photographs and this one has none.", nameof(file));

    using var output = new MemoryStream();

    // The header states its own length, then XPB!, then a version, then PhP2.
    var header = new byte[PhotoParadeFile.HeaderSize];
    header[0] = 0;
    header[1] = 0;
    header[2] = 0;
    header[3] = PhotoParadeFile.HeaderSize;
    PhotoParadeFile.Magic.CopyTo(header.AsSpan(PhotoParadeFile.MagicOffset));
    _Version.CopyTo(header.AsSpan(PhotoParadeFile.MagicOffset + 4));
    PhotoParadeFile.SubFormat.CopyTo(header.AsSpan(PhotoParadeFile.SubFormatOffset));
    output.Write(header);

    for (var i = 0; i < file.Photographs.Count; ++i) {
      var photograph = file.Photographs[i];
      var picture = photograph.Embedded;
      if (picture == null || picture.Length == 0)
        throw new ArgumentException("A PhotoParade photograph carries a whole picture file and this one is empty.", nameof(file));

      output.Write(picture);

      // The block opens where a chunk would carry a length and carries a version instead, so the
      // reader steps over the tag and those four bytes rather than reading them as one.
      output.Write(PhotoParadeFile.PictureInfoTag);
      output.Write(_Version[..4]);

      var member = string.Create(CultureInfo.InvariantCulture, $"{i:D4}.jpg");
      _Chunk(output, _PathTag, Encoding.Latin1.GetBytes(member + "\0"));
      _Chunk(output, _TitleTag, Encoding.Latin1.GetBytes((photograph.Title ?? string.Empty) + "\0"));
      _Chunk(output, _MemberTag, Encoding.Latin1.GetBytes(member + "\0"));
      _Chunk(output, PhotoParadeFile.EndTag, []);
    }

    // The album block, whose count of photographs the reader checks the walk against.
    output.Write(PhotoParadeFile.AlbumTag);
    output.Write(_Version[..4]);
    _Chunk(output, PhotoParadeFile.PictureCountTag, [
      (byte)(file.Photographs.Count >> 24), (byte)(file.Photographs.Count >> 16),
      (byte)(file.Photographs.Count >> 8), (byte)file.Photographs.Count
    ]);
    _Chunk(output, PhotoParadeFile.EndTag, []);

    return output.ToArray();
  }

  private static void _Chunk(Stream output, ReadOnlySpan<byte> tag, byte[] payload) {
    output.Write(tag);
    output.WriteByte((byte)(payload.Length >> 24));
    output.WriteByte((byte)(payload.Length >> 16));
    output.WriteByte((byte)(payload.Length >> 8));
    output.WriteByte((byte)payload.Length);
    output.Write(payload);
  }
}
