using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.NeoBookCartoon;

/// <summary>A NeoBook cartoon (.car): two letters, an offset, and a PNG at it.</summary>
/// <remarks>
/// NeoBook was NeoSoft's multimedia authoring tool and a cartoon is one of the animated figures it
/// draws. No description of the file has been published. What is known of it comes from two places
/// that were arrived at separately: TrID's definition, built from three real files, which records
/// <c>53 4E 0C 00</c> at the front and a PNG signature at offset 12; and XnView's own converter,
/// which reads the format and was asked what it wants by handing it files built to a hypothesis.
/// <para/>
/// It wants the two letters <c>SN</c>, then a 32-bit little-endian offset, then a PNG standing at
/// exactly that offset. Files were accepted with the picture at 6, 12, 20 and 300 bytes in with the
/// field agreeing; moving the picture while leaving the field alone was refused, changing the two
/// letters was refused, and putting a JPEG where the PNG goes was refused. Twelve is the offset all
/// three of TrID's real files use.
/// <para/>
/// One picture is read, the one the header points at, which is the one XnView draws — a file built
/// with a second PNG behind the first still reported a single page. What the bytes between the
/// offset and the picture hold is not known; they are not read here and XnView does not read them
/// either.
/// <para/>
/// Writing uses offset 12 for newly encoded images because that is the value in all three observed
/// real files. Re-serialising a parsed or explicitly constructed file preserves its valid stated
/// offset and fills the otherwise unknown gap with zeroes; those zero-filled files were among the
/// variants accepted by XnView during the format probe.
/// <para/>
/// The two letters are not registered as a signature. Reading by bytes alone takes the first format
/// whose signature matches and does not try a second, and two letters as ordinary as these would
/// take files away from formats that really are what they say they are. The reader still requires
/// them; only content sniffing is left out of it.
/// </remarks>
public readonly record struct NeoBookCartoonFile
  : IImageFormatReader<NeoBookCartoonFile>, IImageToRawImage<NeoBookCartoonFile>,
    IImageFromRawImage<NeoBookCartoonFile>, IImageFormatWriter<NeoBookCartoonFile> {

  /// <summary>The two letters a cartoon opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "SN"u8;

  /// <summary>Two letters and the offset word.</summary>
  public const int HeaderSize = 6;

  /// <summary>Where every observed real NeoBook cartoon starts its PNG.</summary>
  public const int DefaultPictureOffset = 12;

  static string IImageFormatMetadata<NeoBookCartoonFile>.PrimaryExtension => ".car";
  static string[] IImageFormatMetadata<NeoBookCartoonFile>.FileExtensions => [".car"];
  static NeoBookCartoonFile IImageFormatReader<NeoBookCartoonFile>.FromSpan(ReadOnlySpan<byte> data) => NeoBookCartoonReader.FromSpan(data);
  static byte[] IImageFormatWriter<NeoBookCartoonFile>.ToBytes(NeoBookCartoonFile file) => NeoBookCartoonWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<NeoBookCartoonFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Where the header says the picture stands.</summary>
  public int PictureOffset { get; init; }

  /// <summary>The PNG the cartoon carries, whole.</summary>
  public byte[] Picture { get; init; }

  public static RawImage ToRawImage(NeoBookCartoonFile file) {
    if (file.Picture == null)
      throw new InvalidOperationException("No picture was read.");

    return PngFile.ToRawImage(PngReader.FromSpan(file.Picture));
  }

  public static NeoBookCartoonFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      PictureOffset = DefaultPictureOffset,
      Picture = PngWriter.ToBytes(PngFile.FromRawImage(image)),
    };
  }
}
