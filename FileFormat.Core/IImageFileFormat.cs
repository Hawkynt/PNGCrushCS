using System.IO;

namespace FileFormat.Core;

/// <summary>Defines a type that can be read from a file, converted to/from <see cref="RawImage"/>, and written to bytes.</summary>
public interface IImageFileFormat<TSelf> where TSelf : IImageFileFormat<TSelf> {

  /// <summary>The canonical file extension for this format (e.g. ".png").</summary>
  static abstract string PrimaryExtension { get; }

  /// <summary>All recognized file extensions for this format (e.g. [".png"]).</summary>
  static abstract string[] FileExtensions { get; }

  /// <summary>Reads a file from disk into the in-memory representation.</summary>
  static abstract TSelf FromFile(FileInfo file);

  /// <summary>Converts the in-memory representation to a platform-independent <see cref="RawImage"/>.</summary>
  static abstract RawImage ToRawImage(TSelf file);

  /// <summary>Creates the in-memory representation from a platform-independent <see cref="RawImage"/>.</summary>
  static abstract TSelf FromRawImage(RawImage image);

  /// <summary>Serializes the in-memory representation to a byte array.</summary>
  static abstract byte[] ToBytes(TSelf file);
}
