namespace FileFormat.Core;

/// <summary>Serializes the in-memory file representation to bytes. Use <see cref="FormatIO"/> for Stream overloads.</summary>
public interface IImageFormatWriter<TSelf> where TSelf : IImageFormatWriter<TSelf> {

  /// <summary>Serializes the format to a byte array.</summary>
  static abstract byte[] ToBytes(TSelf file);
}
