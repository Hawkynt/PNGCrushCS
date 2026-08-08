using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;
using FileFormat.Jpeg;

namespace FileFormat.PhotoParade;

/// <summary>A PhotoParade slide show (.php), and the photographs in its album.</summary>
/// <remarks>
/// Twenty-four bytes of header — its own length, then <c>XPB!</c>, a version and <c>PhP2</c> — and
/// then the member files one after another with no headers between them. What stands between them
/// instead are description blocks in a grammar of a four-letter tag, a big-endian length and that
/// many bytes, each block ending in a chunk tagged <c>fini</c>.
/// <para/>
/// The album's directory is those blocks. A <c>PNFO</c> block describes one photograph — the path it
/// was taken from, its title, its caption, its date, and the member name <c>0000.jpg</c> upwards —
/// and it stands immediately <em>after</em> the photograph it describes. So a photograph is the JPEG
/// whose own markers run out exactly where the next <c>PNFO</c> begins, and that agreement is what
/// finds it: nothing is searched for by signature and no length is believed on its own.
/// <para/>
/// Two further counts confirm the walk rather than being trusted instead of it. The <c>LBUM</c>
/// block at the end states <c>NUMP</c>, the number of photographs, and it equals the number of
/// <c>PNFO</c> blocks in all seven samples; and the member names run <c>0000.jpg</c> upwards with no
/// gap.
/// <para/>
/// Taking the first picture in the file would be wrong in every one of the seven. What comes before
/// the first photograph is the theme: an 800 by 600 backdrop in two of them, a five-pixel border
/// tile in three, a preview and a thumbnail in another. The album begins where the first
/// <c>PNFO</c> says it does.
/// <para/>
/// Writing puts the album back: each photograph followed by the block describing it, and the album
/// block stating how many there are. What it does not put back is the theme — the last kilobyte or
/// two of a real file is a compressed listing of the theme's own members in a scheme nothing here has
/// worked out, and the backdrop and the border tile in front of the first photograph belong to it. So
/// the file holds the album and is not a slide show anything would run.
/// </remarks>
public sealed class PhotoParadeFile
  : IImageFormatReader<PhotoParadeFile>, IImageToRawImage<PhotoParadeFile>,
    IImageFromRawImage<PhotoParadeFile>, IImageFormatWriter<PhotoParadeFile>,
    IMultiImageFileFormat<PhotoParadeFile> {

  /// <summary>The four bytes at offset four, after the header states its own length.</summary>
  public static ReadOnlySpan<byte> Magic => "XPB!"u8;

  /// <summary>Where the signature sits, and how long the header it belongs to is.</summary>
  public const int MagicOffset = 4, HeaderSize = 24;

  /// <summary>The second signature, which stands after the version.</summary>
  public static ReadOnlySpan<byte> SubFormat => "PhP2"u8;

  /// <summary>Where that second signature sits.</summary>
  public const int SubFormatOffset = 16;

  /// <summary>The tag opening a photograph's description block.</summary>
  public static ReadOnlySpan<byte> PictureInfoTag => "PNFO"u8;

  /// <summary>The tag opening the album's own description block.</summary>
  public static ReadOnlySpan<byte> AlbumTag => "LBUM"u8;

  /// <summary>The tag closing any block.</summary>
  public static ReadOnlySpan<byte> EndTag => "fini"u8;

  /// <summary>The chunk in the album block stating how many photographs there are.</summary>
  public static ReadOnlySpan<byte> PictureCountTag => "NUMP"u8;

  /// <summary>A tag and its big-endian length.</summary>
  public const int ChunkHeaderSize = 8;

  static string IImageFormatMetadata<PhotoParadeFile>.PrimaryExtension => ".php";
  static string[] IImageFormatMetadata<PhotoParadeFile>.FileExtensions => [".php"];
  static PhotoParadeFile IImageFormatReader<PhotoParadeFile>.FromSpan(ReadOnlySpan<byte> data) => PhotoParadeReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PhotoParadeFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<PhotoParadeFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PhotoParadeFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < SubFormatOffset + 4)
      return null;

    return header.Slice(MagicOffset, 4).SequenceEqual(Magic) && header.Slice(SubFormatOffset, 4).SequenceEqual(SubFormat)
      ? true
      : null;
  }

  /// <summary>One photograph: what the album calls it, and the JPEG itself.</summary>
  public readonly record struct Photograph(string Title, byte[] Embedded);

  /// <summary>The album, in the order the description blocks give.</summary>
  public IReadOnlyList<Photograph> Photographs { get; init; } = [];

  public static int ImageCount(PhotoParadeFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Photographs.Count;
  }

  public static RawImage ToRawImage(PhotoParadeFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Photographs.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return EmbeddedPictureReader.Decode(file.Photographs[index].Embedded);
  }

  public static RawImage ToRawImage(PhotoParadeFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Photographs.Count == 0)
      throw new InvalidDataException("A PhotoParade slide show holds no photographs.");

    return ToRawImage(file, 0);
  }

  /// <summary>An album of one photograph, which is this picture.</summary>
  public static PhotoParadeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Photographs = [new("Photograph", JpegWriter.ToBytes(JpegFile.FromRawImage(image)))] };
  }

  static byte[] IImageFormatWriter<PhotoParadeFile>.ToBytes(PhotoParadeFile file) => PhotoParadeWriter.ToBytes(file);
}
