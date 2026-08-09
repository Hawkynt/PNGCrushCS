using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Hta;

/// <summary>A Hemera Thumbs file (.hta): a directory of whole PNG files carried inside one file.</summary>
/// <remarks>
/// Two descriptions of this name exist and they had to be reconciled before anything could be
/// written down. deark's <c>hta</c> module reads it as an archive: eight bytes of signature, a
/// version that has to be 100, a count, and then a directory of position and length pairs, one pair
/// per member, each member a whole picture file. XnView's reader (the function its format table
/// pairs with the name <c>hta</c>) does something narrower: it compares only the first four bytes,
/// <c>89 H T A</c>, steps sixty more bytes forward without looking at them, and from byte 64 onwards
/// slides a four-byte window through the rest of the file counting occurrences of the PNG signature.
/// The page it was asked for is the offset it seeks back to, and a PNG decoder takes over there. It
/// never reads the version, the count or the directory at all.
/// <para/>
/// A file built to deark's description alone is refused by XnView, and the reason is the sixty-byte
/// step: with one member the directory ends at byte 24, the PNG starts there, and the scan that
/// begins at byte 64 walks straight past it. Both were asked, one byte at a time: a member at 63 is
/// refused and a member at 64 is read, so the two only ever disagree about where the first member is
/// allowed to stand. A file whose first member stands at or after byte 64 satisfies both, and that
/// is the file this reader accepts and the one the tests build.
/// <para/>
/// So the directory is deark's and the constraint is XnView's, and what ties the two together is
/// that each entry's stated length has to be the length the member itself declares — a PNG's own
/// chunk chain, walked from its signature to its IEND, has to end exactly where the directory says
/// it does. That is what makes this a Hemera Thumbs file rather than any file with those four bytes
/// at the front; a fixed offset guessed from one sample would not.
/// <para/>
/// Confirmed against XnView's own converter: a file built this way is reported as
/// <c>Name : hta, Format : Hemera Thumbs</c> at the member's size and depth, two members are
/// reported as two pages, and the pixels it writes back out are the member's pixels byte for byte.
/// </remarks>
[FormatMagicBytes([0x89, (byte)'H', (byte)'T', (byte)'A', 0x0D, 0x0A, 0x1A, 0x0A])]
public sealed class HtaFile
  : IImageFormatReader<HtaFile>, IImageToRawImage<HtaFile>, IMultiImageFileFormat<HtaFile> {

  /// <summary>The eight bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x89, (byte)'H', (byte)'T', (byte)'A', 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>The only version the format is known in.</summary>
  public const int SupportedVersion = 100;

  /// <summary>Signature, version and count, with the directory starting behind them.</summary>
  public const int DirectoryOffset = 16;

  /// <summary>A position and a length, both unsigned and little-endian.</summary>
  public const int DirectoryEntrySize = 8;

  /// <summary>
  /// The byte the first member has to stand at or after. XnView steps sixty bytes past the four it
  /// compares before it starts looking for a member, so anything earlier is invisible to it.
  /// </summary>
  public const int FirstMemberOffset = 64;

  /// <summary>More members than a thumbnail catalogue would ever hold; a guard against a wild count.</summary>
  public const int MaximumMemberCount = 65536;

  static string IImageFormatMetadata<HtaFile>.PrimaryExtension => ".hta";
  static string[] IImageFormatMetadata<HtaFile>.FileExtensions => [".hta"];
  static FormatCapability IImageFormatMetadata<HtaFile>.Capabilities => FormatCapability.MultiImage;
  static HtaFile IImageFormatReader<HtaFile>.FromSpan(ReadOnlySpan<byte> data) => HtaReader.FromSpan(data);

  /// <summary>The members, each a whole PNG file exactly as long as the directory said it was.</summary>
  public IReadOnlyList<byte[]> Members { get; init; } = [];

  /// <summary>The version the header carries, which is 100 in every file this reads.</summary>
  public int Version { get; init; } = SupportedVersion;

  public static RawImage ToRawImage(HtaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return ToRawImage(file, 0);
  }

  public static int ImageCount(HtaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Members.Count;
  }

  public static RawImage ToRawImage(HtaFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Members.Count)
      throw new ArgumentOutOfRangeException(nameof(index), $"A Hemera Thumbs file of {file.Members.Count} members has no member {index}.");

    return PngFile.ToRawImage(PngReader.FromBytes(file.Members[index]));
  }
}
