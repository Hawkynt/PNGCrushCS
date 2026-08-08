using System;
using FileFormat.Core;

namespace FileFormat.BodyPaint3D;

/// <summary>A BodyPaint 3D texture (.b3d), the painted bitmap a Cinema 4D material carries.</summary>
/// <remarks>
/// The file opens with <c>AC4DBody</c> and is then a tagged value stream, big-endian, with no
/// length on any record: a record begins with a tag of 1 carrying its class and ends with a tag of
/// 2, and a reader has to walk the stream rather than seek through it. The classes are four-letter
/// names beginning <c>Bd</c> — <c>BdTx</c> states the size, <c>BdVx</c> carries the pixels,
/// <c>BdLy</c> describes a layer.
/// <para/>
/// <c>BdVx</c> holds one record per scanline, each of them PackBits over exactly one row of one
/// channel, and the rows arrive interleaved by channel rather than in planes: for a three-channel
/// texture the stream is the red row, the green row and the blue row of picture row zero, then the
/// three rows of picture row one. That is settled numerically rather than by eye — in the samples
/// with a grey subject the three rows of a triple are byte-identical to each other and differ from
/// the next triple, which the plane-ordered reading cannot produce.
/// <para/>
/// A file that has layers carries the same bitmap twice, once as the flattened document and once as
/// the single layer; they are byte-identical wherever both are present, and one sample's layer
/// states a sentinel rectangle and carries no scanlines at all. So the picture taken is the first
/// <c>BdVx</c> that has scanlines and whose rectangle is the size <c>BdTx</c> states, which is the
/// flattened document in every sample.
/// <para/>
/// Nothing on this machine reads the format and no specification of it is published, so what stands
/// behind the layout is the corpus: all ten distinct samples walk from the signature to the last
/// record and land on the end of the file exactly, with no tag left unaccounted for, and every one
/// of the 32,400 scanlines decompresses to exactly the width the header states. It does not write:
/// a texture with no document behind it is not a document.
/// </remarks>
public readonly record struct BodyPaint3DFile : IImageFormatReader<BodyPaint3DFile>, IImageToRawImage<BodyPaint3DFile> {

  /// <summary>The eight bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "AC4DBody"u8;

  /// <summary>Tag of a record's opening, which carries the class and a subtype.</summary>
  public const byte TagBegin = 0x01;

  /// <summary>Tag of a record's close.</summary>
  public const byte TagEnd = 0x02;

  /// <summary>Tag of a compressed scanline: a method byte and then a byte array.</summary>
  public const byte TagScanline = 0x0C;

  /// <summary>Tag of a signed 32-bit value.</summary>
  public const byte TagInt32 = 0x0F;

  /// <summary>Tag of a 32-bit float.</summary>
  public const byte TagFloat32 = 0x13;

  /// <summary>Tag of a single byte.</summary>
  public const byte TagByte = 0x15;

  /// <summary>Tag of a length-prefixed byte array.</summary>
  public const byte TagByteArray = 0x80;

  /// <summary>Tag of a length-prefixed UTF-16 big-endian string.</summary>
  public const byte TagWideString = 0x82;

  /// <summary>Class of the record stating the texture's size.</summary>
  public const uint ClassTexture = 0x42645478; // BdTx

  /// <summary>Class of the record carrying pixels.</summary>
  public const uint ClassBitmap = 0x42645678; // BdVx

  /// <summary>The only scanline compression any sample uses, PackBits.</summary>
  public const byte MethodPackBits = 1;

  /// <summary>Channels a texture may carry: one grey, or red, green and blue.</summary>
  public const int GrayPlanes = 1, RgbPlanes = 3;

  /// <summary>The largest side accepted, past which the record is not a texture header.</summary>
  public const int MaxDimension = 1 << 16;

  static string IImageFormatMetadata<BodyPaint3DFile>.PrimaryExtension => ".b3d";
  static string[] IImageFormatMetadata<BodyPaint3DFile>.FileExtensions => [".b3d"];
  static BodyPaint3DFile IImageFormatReader<BodyPaint3DFile>.FromSpan(ReadOnlySpan<byte> data) => BodyPaint3DReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BodyPaint3DFile>.VideoModes => [
    new("Grayscale", [(IntegerRange.Any, IntegerRange.Any)], [256]),
    new("Color", [(IntegerRange.Any, IntegerRange.Any)], [16777216]),
  ];

  static bool? IImageFormatMetadata<BodyPaint3DFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 8 ? null : header[..8].SequenceEqual(Magic);

  /// <summary>Pixels across, as the texture header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the texture header states.</summary>
  public int Height { get; init; }

  /// <summary>Channels the bitmap record carries: 1 for grey, 3 for colour.</summary>
  public int Planes { get; init; }

  /// <summary>The decompressed pixels, one byte per channel, channels interleaved per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Hands over the decompressed picture, grey where the file carries one channel.</summary>
  public static RawImage ToRawImage(BodyPaint3DFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Planes == GrayPlanes ? PixelFormat.Gray8 : PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
