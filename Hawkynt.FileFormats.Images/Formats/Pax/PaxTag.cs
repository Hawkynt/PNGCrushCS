namespace FileFormat.Pax;

/// <summary>One named metadata record of a Pick Ax file.</summary>
/// <param name="Name">The four-character tag name.</param>
/// <param name="Data">The record's bytes. A tag occupies whole eight-byte blocks and states that padded size, so data written at another length comes back with zeros after it.</param>
public readonly record struct PaxTag(string Name, byte[] Data) {

  /// <summary>Bytes in a tag name.</summary>
  public const int NameSize = 4;
}
