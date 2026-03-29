using System;

namespace FileFormat.DjVu.Codec;

/// <summary>
/// Context modeling for IW44 bitplane coding.
/// Provides separate ZP contexts for significance, sign, and refinement coding
/// of wavelet coefficients across different subbands and bitplanes.
/// </summary>
internal sealed class Iw44BitContext {

  /// <summary>Number of distinct sub-band buckets for context selection.</summary>
  private const int _BUCKET_COUNT = 10;

  /// <summary>Maximum number of bitplanes tracked (must be > _MAX_BITPLANE in encoder/decoder).</summary>
  private const int _MAX_BITPLANES = 16;

  /// <summary>Contexts for significance coding (is a zero coefficient becoming significant?).</summary>
  private readonly ZpContext[] _significance = new ZpContext[_BUCKET_COUNT * _MAX_BITPLANES];

  /// <summary>Contexts for sign coding (positive or negative?).</summary>
  private readonly ZpContext[] _sign = new ZpContext[_BUCKET_COUNT];

  /// <summary>Contexts for refinement coding (adding precision to already-significant coefficients).</summary>
  private readonly ZpContext[] _refinement = new ZpContext[_BUCKET_COUNT * _MAX_BITPLANES];

  /// <summary>
  /// Maps a decomposition level and sub-band type to a bucket index.
  /// Sub-band types: 0=LH (horizontal detail), 1=HL (vertical detail), 2=HH (diagonal detail).
  /// Level 0 is the finest (largest), higher levels are coarser.
  /// </summary>
  public static int GetBucket(int level, int subBand) {
    var bucket = level * 3 + subBand;
    return Math.Clamp(bucket, 0, _BUCKET_COUNT - 1);
  }

  /// <summary>Returns a reference to the significance context for the given bucket and bitplane.</summary>
  public ref ZpContext Significance(int bucket, int bitplane) {
    var idx = Math.Clamp(bucket, 0, _BUCKET_COUNT - 1) * _MAX_BITPLANES
              + Math.Clamp(bitplane, 0, _MAX_BITPLANES - 1);
    return ref _significance[idx];
  }

  /// <summary>Returns a reference to the sign context for the given bucket.</summary>
  public ref ZpContext Sign(int bucket) => ref _sign[Math.Clamp(bucket, 0, _BUCKET_COUNT - 1)];

  /// <summary>Returns a reference to the refinement context for the given bucket and bitplane.</summary>
  public ref ZpContext Refinement(int bucket, int bitplane) {
    var idx = Math.Clamp(bucket, 0, _BUCKET_COUNT - 1) * _MAX_BITPLANES
              + Math.Clamp(bitplane, 0, _MAX_BITPLANES - 1);
    return ref _refinement[idx];
  }
}
