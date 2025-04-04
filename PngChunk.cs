using System.Text;

/// <summary>
/// Represents a PNG chunk. Uses a record struct for value semantics and immutability.
/// </summary>
internal readonly record struct PngChunk(uint Length, string Type, byte[] Data, uint Crc) {
  public static PngChunk Create(string type, byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (type.Length != 4 || !type.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
      throw new ArgumentException("Invalid PNG chunk type.", nameof(type));

    var typeBytes = Encoding.ASCII.GetBytes(type);
    var crc = PngParser.CalculateCrc(typeBytes, data);
    return new PngChunk((uint)data.Length, type, data, crc);
  }
}
