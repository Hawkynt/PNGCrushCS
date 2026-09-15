using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.RealVideo;

/// <summary>Which RealVideo generation a stream is coded with.</summary>
internal enum RealVideoGeneration {
  Unknown = 0,
  RealVideo10 = 1,
  RealVideo20 = 2,
  RealVideo30 = 3,
  RealVideo40 = 4,
}

/// <summary>What a RealVideo stream's codec-private version word says about its bitstream.</summary>
/// <remarks>
/// The word is split the same way the reference decoder does: four major bits, eight minor bits and
/// eight micro bits. In particular, <c>0x10001000</c> is major 1, minor 0, micro 1 — it is not
/// "revision 1" in the minor field. That distinction matters because RV10 micro 3 switches the intra
/// DC syntax to RealVideo-specific predictive VLCs.
/// </remarks>
/// <param name="Generation">The major RealVideo generation.</param>
/// <param name="Version">The complete 32-bit word.</param>
/// <param name="Minor">The eight-bit minor version.</param>
/// <param name="Micro">The eight-bit micro version.</param>
internal readonly record struct RealVideoBitstreamVersion(
  RealVideoGeneration Generation, uint Version, int Minor, int Micro) {

  private const int _VERSION_OFFSET = 4;

  internal static RealVideoBitstreamVersion Read(ReadOnlySpan<byte> codecPrivateData, RealVideoGeneration fromTag) {
    if (codecPrivateData.Length < _VERSION_OFFSET + 4)
      return new(fromTag, 0, 0, 0);

    var version = BinaryPrimitives.ReadUInt32BigEndian(codecPrivateData[_VERSION_OFFSET..]);
    var generation = (RealVideoGeneration)(version >> 28);
    if (generation != fromTag)
      return new(RealVideoGeneration.Unknown, version, 0, 0);

    return new(
      generation,
      version,
      (int)((version >> 20) & 0xFF),
      (int)((version >> 12) & 0xFF));
  }

  internal const int IMPLEMENTED_MINOR = 0;
  internal const int IMPLEMENTED_MICRO = 0;
}