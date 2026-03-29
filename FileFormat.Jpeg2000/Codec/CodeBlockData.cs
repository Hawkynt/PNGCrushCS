namespace FileFormat.Jpeg2000.Codec;

/// <summary>Compressed data and metadata for a single code-block decoded from tier-2 packets.</summary>
internal sealed class CodeBlockData {
  /// <summary>Index identifying which subband this code-block belongs to.</summary>
  public int SubbandIndex { get; init; }

  /// <summary>Code-block column index within the subband.</summary>
  public int CodeBlockX { get; init; }

  /// <summary>Code-block row index within the subband.</summary>
  public int CodeBlockY { get; init; }

  /// <summary>Number of coding passes to decode.</summary>
  public int NumCodingPasses { get; init; }

  /// <summary>Number of leading zero bit-planes (from tag tree).</summary>
  public int ZeroBitPlanes { get; init; }

  /// <summary>MQ-coded compressed data for this code-block.</summary>
  public byte[] CompressedData { get; init; } = [];
}
