namespace FileFormat.Core;

/// <summary>Creates the in-memory file representation from a platform-independent <see cref="RawImage"/>. Optional — read-only formats omit this.</summary>
public interface IImageFromRawImage<TSelf> where TSelf : IImageFromRawImage<TSelf> {

  /// <summary>Creates the in-memory representation from a platform-independent <see cref="RawImage"/>.</summary>
  static abstract TSelf FromRawImage(RawImage image);
}
