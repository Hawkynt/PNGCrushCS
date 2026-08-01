using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.RicohJ6i;

/// <summary>In-memory representation of a Ricoh J6I camera picture.</summary>
/// <remarks>
/// A 512-byte block of camera state — the maker's name, the mode the picture was taken in, the
/// timestamps — and then an ordinary JPEG. The camera wrote its own header rather than using the
/// application markers JPEG provides for exactly this, which is why the file needs a reader of its
/// own and why that reader has almost nothing to do.
/// </remarks>
public readonly record struct RicohJ6iFile
  : IImageFormatReader<RicohJ6iFile>, IImageToRawImage<RicohJ6iFile>,
    IImageFromRawImage<RicohJ6iFile>, IImageFormatWriter<RicohJ6iFile> {

  /// <summary>Bytes of camera state before the picture.</summary>
  public const int HeaderSize = 512;

  /// <summary>The first byte every file starts with.</summary>
  public const byte SignatureFirst = 0x80;

  /// <summary>The second.</summary>
  public const byte SignatureSecond = 0x3E;

  /// <summary>The text that follows them.</summary>
  public const string Marker = "DSCIM";

  static string IImageFormatMetadata<RicohJ6iFile>.PrimaryExtension => ".j6i";
  static string[] IImageFormatMetadata<RicohJ6iFile>.FileExtensions => [".j6i"];
  static RicohJ6iFile IImageFormatReader<RicohJ6iFile>.FromSpan(ReadOnlySpan<byte> data) => RicohJ6iReader.FromSpan(data);
  static byte[] IImageFormatWriter<RicohJ6iFile>.ToBytes(RicohJ6iFile file) => RicohJ6iWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<RicohJ6iFile>.VideoModes => [
    new("Default", [(768, 480)], [16777216])
  ];

  /// <summary>The camera state, kept whole so that writing a file back does not invent one.</summary>
  public byte[] Header { get; init; }

  /// <summary>The picture, which is an ordinary JPEG.</summary>
  public byte[] JpegData { get; init; }

  public int Width { get; init; }

  public int Height { get; init; }

  public static RawImage ToRawImage(RicohJ6iFile file)
    => JpegFile.ToRawImage(JpegReader.FromSpan(file.JpegData ?? []));

  public static RicohJ6iFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(image));
    var header = new byte[HeaderSize];
    header[0] = SignatureFirst;
    header[1] = SignatureSecond;
    System.Text.Encoding.ASCII.GetBytes(Marker).CopyTo(header, 2);

    return new() {
      Header = header,
      JpegData = jpeg,
      Width = image.Width,
      Height = image.Height,
    };
  }
}
