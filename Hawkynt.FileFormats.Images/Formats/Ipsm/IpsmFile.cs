using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Ipsm;

/// <summary>In-memory representation of an IPSM panorama (.pan).</summary>
/// <remarks>
/// A tagged container, not a picture format of its own. Sixteen bytes of header — <c>IPSM</c>, the
/// length of the whole file, and how many chunks follow — and then that many directory entries of
/// sixteen bytes each: a four-letter tag, the offset its data begins at, the length of that data,
/// and a spare. The sample has two, <c>INIT</c> and <c>BTMP</c>, and the second is an ordinary JPEG.
/// <para/>
/// Nothing here is guessed. The stated file length is the file's length to the byte, and the
/// <c>BTMP</c> chunk's offset and length together account for everything after the directory, which
/// is what says the entries are being read as the format means them.
/// </remarks>
public readonly record struct IpsmFile
  : IImageFormatReader<IpsmFile>, IImageToRawImage<IpsmFile>,
    IImageFromRawImage<IpsmFile>, IImageFormatWriter<IpsmFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'I', (byte)'P', (byte)'S', (byte)'M'];

  /// <summary>The tag naming the chunk that holds the picture.</summary>
  public static ReadOnlySpan<byte> BitmapTag => [(byte)'B', (byte)'T', (byte)'M', (byte)'P'];

  /// <summary>The tag the sample carries ahead of the picture, sixteen bytes of nothing.</summary>
  public static ReadOnlySpan<byte> InitTag => [(byte)'I', (byte)'N', (byte)'I', (byte)'T'];

  /// <summary>The magic, the file length, the chunk count and a spare.</summary>
  public const int HeaderSize = 16;

  /// <summary>A tag, an offset, a length and a spare.</summary>
  public const int DirectoryEntrySize = 16;

  /// <summary>The two chunks this writes, which are the two the sample has.</summary>
  internal const int WrittenChunkCount = 2;

  /// <summary>How long the <c>INIT</c> chunk is, in the one file there is to go by.</summary>
  internal const int InitLength = 16;

  static string IImageFormatMetadata<IpsmFile>.PrimaryExtension => ".pan";
  static string[] IImageFormatMetadata<IpsmFile>.FileExtensions => [".pan"];
  static IpsmFile IImageFormatReader<IpsmFile>.FromSpan(ReadOnlySpan<byte> data) => IpsmReader.FromSpan(data);
  static byte[] IImageFormatWriter<IpsmFile>.ToBytes(IpsmFile file) => IpsmWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<IpsmFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The JPEG the <c>BTMP</c> chunk carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(IpsmFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded ?? throw new InvalidDataException("An IPSM file carries no BTMP chunk.")));

  public static IpsmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { Embedded = JpegWriter.ToBytes(JpegFile.FromRawImage(image)) };
  }
}
