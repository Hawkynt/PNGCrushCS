using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.FunPhotor;

/// <summary>In-memory representation of a FunPhotor frame (.fpr).</summary>
/// <remarks>
/// Four bytes of length and then an ordinary PNG. Nothing here decodes anything itself; the whole
/// format is the wrapper, and all three samples come out of the PNG reader matching XnView exactly.
/// </remarks>
public readonly record struct FunPhotorFile
  : IImageFormatReader<FunPhotorFile>, IImageToRawImage<FunPhotorFile>,
    IImageFromRawImage<FunPhotorFile>, IImageFormatWriter<FunPhotorFile> {

  static string IImageFormatMetadata<FunPhotorFile>.PrimaryExtension => ".fpr";
  static string[] IImageFormatMetadata<FunPhotorFile>.FileExtensions => [".fpr"];
  static FunPhotorFile IImageFormatReader<FunPhotorFile>.FromSpan(ReadOnlySpan<byte> data) => FunPhotorReader.FromSpan(data);
  static byte[] IImageFormatWriter<FunPhotorFile>.ToBytes(FunPhotorFile file) => FunPhotorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FunPhotorFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Bytes of length ahead of the PNG.</summary>
  internal const int HeaderSize = 4;

  /// <summary>The PNG the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(FunPhotorFile file)
    => PngFile.ToRawImage(PngReader.FromBytes(file.Embedded ?? throw new InvalidDataException("A FunPhotor frame carries no picture.")));

  public static FunPhotorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { Embedded = PngWriter.ToBytes(PngFile.FromRawImage(image)) };
  }
}
