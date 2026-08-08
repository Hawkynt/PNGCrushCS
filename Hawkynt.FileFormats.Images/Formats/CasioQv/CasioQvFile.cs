using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.CasioQv;

/// <summary>A picture off a Casio QV camera (.cam).</summary>
/// <remarks>
/// The container is the same for every model: four bytes <c>07 20 4D 4D</c>, a count of areas, then
/// that many sixteen-byte descriptors of an area number and a length, and then the areas themselves
/// end to end in the order the descriptors gave. Nothing states an offset — the offsets are the
/// running sum of the lengths, and that sum accounting for the file exactly is what says the table
/// has been read right.
/// <para/>
/// What differs between models is what an area holds, and that is read from the bytes rather than
/// from the file's name. Area 12 on the later cameras is a whole JFIF and is handed over as it
/// stands. Area 3 on the QV-10 generation is a JPEG with everything but the entropy data taken out
/// of it: the payload is the area number, the three scan lengths, the two quantisation tables the
/// camera used, and then the luminance, blue-difference and red-difference scans one after another.
/// <para/>
/// The frames that surround them — the markers, the Huffman tables and the frame header — are the
/// ones in <c>cam2jpgtab.h</c> from itojun's <c>qvplay</c>, which is the published reference for
/// this format and the only one. The quantisation tables themselves are not reconstructed: only the
/// five-byte segment headers are constant, and the sixty-four values in each come out of the file.
/// <para/>
/// The frame header says 480 by 240 at three-by-two luminance sampling, and the picture is handed
/// over on that grid because that is the grid the file stores. The camera's pixels are not square —
/// <c>qvplay</c>'s own examples scale the result to 320 by 240 before looking at it — but resampling
/// is a decision for whatever displays the picture, and no other reader here corrects an aspect.
/// <para/>
/// Writing emits the later cameras' shape: the container with one area 12 holding a whole JFIF. The
/// QV-10 generation's stripped area is not written back — putting a stream into that shape means
/// coding three separate scans on the camera's own sampling grid, and the picture is no better
/// recorded for it than by the whole stream the same reader accepts.
/// </remarks>
public readonly record struct CasioQvFile
  : IImageFormatReader<CasioQvFile>, IImageToRawImage<CasioQvFile>,
    IImageFromRawImage<CasioQvFile>, IImageFormatWriter<CasioQvFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x07, 0x20, 0x4D, 0x4D];

  /// <summary>Magic, then the count of areas.</summary>
  public const int TableOffset = 6;

  /// <summary>An area number, a length, and ten bytes nothing here reads.</summary>
  public const int DescriptorSize = 16;

  /// <summary>The most areas a table may describe, past which the file is not one of these.</summary>
  public const int MaxAreaCount = 64;

  /// <summary>The area holding a QV-10 generation stream with its tables taken out.</summary>
  public const int AreaStrippedJpeg = 3;

  /// <summary>The area holding a whole JFIF, which the later cameras write.</summary>
  public const int AreaWholeJpeg = 12;

  /// <summary>Area number, then the three scan lengths, all sixteen-bit big-endian.</summary>
  public const int StrippedHeaderSize = 8;

  /// <summary>The two quantisation tables the payload carries, sixty-four values each.</summary>
  public const int QuantTableSize = 64;

  static string IImageFormatMetadata<CasioQvFile>.PrimaryExtension => ".cam";
  static string[] IImageFormatMetadata<CasioQvFile>.FileExtensions => [".cam"];
  static CasioQvFile IImageFormatReader<CasioQvFile>.FromSpan(ReadOnlySpan<byte> data) => CasioQvReader.FromSpan(data);
  static byte[] IImageFormatWriter<CasioQvFile>.ToBytes(CasioQvFile file) => CasioQvWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CasioQvFile>.VideoModes => [
    new("QV-10", [(480, 240)], [16777216]),
    new("QV-5000", [(IntegerRange.Any, IntegerRange.Any)], [16777216]),
  ];

  static bool? IImageFormatMetadata<CasioQvFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 4 ? null : header[..4].SequenceEqual(Magic);

  /// <summary>Pixels across, as the frame header of the stream states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the frame header of the stream states.</summary>
  public int Height { get; init; }

  /// <summary>Whether the stream had to be reassembled or stood whole in the file.</summary>
  public bool WasReassembled { get; init; }

  /// <summary>The JPEG, whole as the file held it or put back together from the area.</summary>
  public byte[] Jpeg { get; init; }

  /// <summary>Decodes the stream the file carries.</summary>
  public static RawImage ToRawImage(CasioQvFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Jpeg ?? throw new InvalidDataException("A Casio QV picture carries no stream.")));

  /// <summary>Codes the picture as the whole JFIF the later cameras keep in area 12.</summary>
  public static CasioQvFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      WasReassembled = false,
      Jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(image)),
    };
  }
}
