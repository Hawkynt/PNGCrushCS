using System;
using FileFormat.Core;

namespace FileFormat.X3f;

/// <summary>In-memory representation of a Sigma/Foveon X3F raw file (.x3f).</summary>
/// <remarks>
/// A sectioned container. Two hundred and thirty-two bytes of header — <c>FOVb</c>, a version, an
/// identifier, the size of the sensor and a white balance — and then sections laid end to end, with
/// a directory at the very end of the file that the last four bytes point at. The directory names
/// each section's offset, length and kind; the picture-bearing ones are <c>IMAG</c> and <c>IMA2</c>,
/// and each states its own type, storage format, width, height and row stride.
/// <para/>
/// The storage formats are not all the same thing. Some cameras put a full-size JPEG in one of these
/// sections and their sensor data in another; the Polaroid x530 does exactly that, and its JPEG is
/// the picture at the size the camera says it took. Others — the Sigma bodies — store only Foveon
/// data, coded with a Huffman scheme and needing a large block of per-camera correction values to
/// turn three stacked layers into colour, and carry nothing else above thumbnail size.
/// <para/>
/// Only the two plain storages are read: a JPEG stream, and uncompressed twenty-four bit samples.
/// A file whose largest readable picture is a small fraction of the size it claims is refused rather
/// than answered with its preview, because a preview drawn as the picture is the wrong answer given
/// confidently.
/// </remarks>
public readonly record struct X3fFile : IImageFormatReader<X3fFile>, IImageToRawImage<X3fFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'F', (byte)'O', (byte)'V', (byte)'b'];

  /// <summary>The four bytes the directory opens with.</summary>
  public static ReadOnlySpan<byte> DirectoryMagic => [(byte)'S', (byte)'E', (byte)'C', (byte)'d'];

  /// <summary>Where the sensor's column count sits in the header.</summary>
  public const int ColumnsField = 0x1C;

  /// <summary>Where the sensor's row count sits in the header.</summary>
  public const int RowsField = 0x20;

  /// <summary>The header runs to here, which is where the first section starts.</summary>
  public const int HeaderSize = 232;

  /// <summary>A directory entry is an offset, a length and a four-letter kind.</summary>
  public const int DirectoryEntrySize = 12;

  /// <summary>Section magic, version, type, format, columns, rows and row stride.</summary>
  public const int ImageSectionHeaderSize = 28;

  /// <summary>Storage format for a section holding an ordinary JPEG stream.</summary>
  public const int FormatJpeg = 18;

  /// <summary>Storage format for a section holding uncompressed three-byte samples.</summary>
  public const int FormatRgb24 = 3;

  static string IImageFormatMetadata<X3fFile>.PrimaryExtension => ".x3f";
  static string[] IImageFormatMetadata<X3fFile>.FileExtensions => [".x3f"];
  static X3fFile IImageFormatReader<X3fFile>.FromSpan(ReadOnlySpan<byte> data) => X3fReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<X3fFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in RGB24 interleaved order.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(X3fFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }
}
