namespace FileFormat.Jnx;

/// <summary>One tile of a Garmin JNX map, and the patch of the world it covers.</summary>
/// <remarks>
/// The bounds are kept in the units the file states them in — a signed 32-bit
/// count of 180/0x7FFFFFFF degrees — rather than converted to degrees and back,
/// so a tile read and written again names the same ground it did before.
/// </remarks>
public sealed class JnxTile {

  /// <summary>The tile's picture, as a complete JPEG.</summary>
  /// <remarks>
  /// A JNX stores the tile without its start-of-image marker, which every tile
  /// would otherwise repeat. The two bytes are put back when the tile is read
  /// and taken off again when it is written, so this stays an ordinary JPEG that
  /// any decoder will open.
  /// </remarks>
  public required byte[] JpegData { get; init; }

  public int Width { get; init; }
  public int Height { get; init; }

  public int NorthEastX { get; init; }
  public int NorthEastY { get; init; }
  public int SouthWestX { get; init; }
  public int SouthWestY { get; init; }
}
