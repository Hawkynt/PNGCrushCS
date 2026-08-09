using System;
using FileFormat.Core;

namespace FileFormat.MegaluxFrame;

/// <summary>In-memory representation of a Megalux Frame picture (.frm).</summary>
/// <remarks>
/// Megalux was a video-capture product; a <c>.frm</c> is one grabbed frame written out whole. There
/// are two readings of the format about, and they disagree.
/// <para/>
/// FFmpeg's <c>libavformat/frmdec.c</c> says the header is eight bytes — <c>FRM</c>, a one-byte code
/// choosing between five pixel layouts, then a width and a height as sixteen-bit little-endian
/// numbers — with the picture immediately behind it. XnView reads only one of those five codes, the
/// fourth, and starts the picture sixteen bytes further on, at offset twenty-four.
/// <para/>
/// This reader follows XnView, because XnView is what the pictures here are checked against: its
/// converter is handed a file built to this layout and returns the pixels that were put in, byte for
/// byte, while a file with the picture at offset eight comes back shifted by four rows. The sixteen
/// bytes between the size and the picture are not read by XnView — every one of them was set in turn
/// and the picture it returns did not change — so they are passed over rather than interpreted.
/// <para/>
/// A code other than four is refused. XnView refuses all four of the others, so there is nothing to
/// check a reading of them against, and the widths their layouts imply differ, so a guess would draw
/// a picture of the wrong shape rather than fail.
/// </remarks>
public readonly record struct MegaluxFrameFile
  : IImageFormatReader<MegaluxFrameFile>, IImageToRawImage<MegaluxFrameFile> {

  /// <summary>The three letters a file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => "FRM"u8;

  /// <summary>The only pixel-layout code that is read: four bytes a pixel, blue first.</summary>
  public const byte SupportedFormatCode = 4;

  /// <summary>Signature, code, width and height.</summary>
  public const int DeclaredHeaderSize = 8;

  /// <summary>Where the picture starts, which is sixteen bytes behind the stated header.</summary>
  public const int PixelDataOffset = 24;

  /// <summary>Bytes one pixel takes in the file: blue, green, red and one that is not used.</summary>
  public const int BytesPerPixel = 4;

  static string IImageFormatMetadata<MegaluxFrameFile>.PrimaryExtension => ".frm";
  static string[] IImageFormatMetadata<MegaluxFrameFile>.FileExtensions => [".frm"];
  static MegaluxFrameFile IImageFormatReader<MegaluxFrameFile>.FromSpan(ReadOnlySpan<byte> data) => MegaluxFrameReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<MegaluxFrameFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<MegaluxFrameFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < DeclaredHeaderSize)
      return null;

    return header[..Signature.Length].SequenceEqual(Signature) && header[3] == SupportedFormatCode;
  }

  /// <summary>Image width in pixels, as the header states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, as the header states it.</summary>
  public int Height { get; init; }

  /// <summary>The picture, three bytes a pixel, red first, one row after another from the top.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MegaluxFrameFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
