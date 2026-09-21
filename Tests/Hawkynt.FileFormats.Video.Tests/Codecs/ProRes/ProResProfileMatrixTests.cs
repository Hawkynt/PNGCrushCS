using System;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// The quantisation weight matrices each profile puts in its frame header.
/// </summary>
/// <remarks>
/// These cannot be checked by decoding what this package writes, and they cannot be checked by an
/// external decoder either: RDD 36 states both matrices in every frame header, so a decoder — ours or
/// anybody's — reads back whatever was written and agrees with it. A profile written with another
/// profile's matrices is a different profile wearing the same four-character code, and every round
/// trip and every oracle in the suite passes on it.
/// <para/>
/// So the matrices are asserted against the values themselves, written out here a second time rather
/// than read from <see cref="ProResProfile"/>, and taken from the frame headers FFmpeg's own encoder
/// produces. Each was read out of a file written by <c>prores_ks</c>: the four 4:2:2 profiles and
/// ProRes 4444 from <c>-profile:v 0</c> to <c>4</c>, and the 4444 XQ luma table from
/// <c>-quant_mat 5</c>, which is the entry of <c>prores_quant_matrices</c> that <c>-profile:v 5</c>
/// names but its profile table does not select.
/// </remarks>
[TestFixture]
public sealed class ProResProfileMatrixTests {

  private static readonly byte[] _ProxyLuma = [
     4,  7,  9, 11, 13, 14, 15, 63,
     7,  7, 11, 12, 14, 15, 63, 63,
     9, 11, 13, 14, 15, 63, 63, 63,
    11, 11, 13, 14, 63, 63, 63, 63,
    11, 13, 14, 63, 63, 63, 63, 63,
    13, 14, 63, 63, 63, 63, 63, 63,
    13, 63, 63, 63, 63, 63, 63, 63,
    63, 63, 63, 63, 63, 63, 63, 63,
  ];

  private static readonly byte[] _ProxyChroma = [
     4,  7,  9, 11, 13, 14, 63, 63,
     7,  7, 11, 12, 14, 63, 63, 63,
     9, 11, 13, 14, 63, 63, 63, 63,
    11, 11, 13, 14, 63, 63, 63, 63,
    11, 13, 14, 63, 63, 63, 63, 63,
    13, 14, 63, 63, 63, 63, 63, 63,
    13, 63, 63, 63, 63, 63, 63, 63,
    63, 63, 63, 63, 63, 63, 63, 63,
  ];

  private static readonly byte[] _Lt = [
     4,  5,  6,  7,  9, 11, 13, 15,
     5,  5,  7,  8, 11, 13, 15, 17,
     6,  7,  9, 11, 13, 15, 15, 17,
     7,  7,  9, 11, 13, 15, 17, 19,
     7,  9, 11, 13, 14, 16, 19, 23,
     9, 11, 13, 14, 16, 19, 23, 29,
     9, 11, 13, 15, 17, 21, 28, 35,
    11, 13, 16, 17, 21, 28, 35, 41,
  ];

  private static readonly byte[] _Standard = [
    4, 4, 5,  5,  6,  7,  7,  9,
    4, 4, 5,  6,  7,  7,  9,  9,
    5, 5, 6,  7,  7,  9,  9, 10,
    5, 5, 6,  7,  7,  9,  9, 10,
    5, 6, 7,  7,  8,  9, 10, 12,
    6, 7, 7,  8,  9, 10, 12, 15,
    6, 7, 7,  9, 10, 11, 14, 17,
    7, 7, 9, 10, 11, 14, 17, 21,
  ];

  private static readonly byte[] _HighQuality = [
    4, 4, 4, 4, 4, 4, 4, 4,
    4, 4, 4, 4, 4, 4, 4, 4,
    4, 4, 4, 4, 4, 4, 4, 4,
    4, 4, 4, 4, 4, 4, 4, 5,
    4, 4, 4, 4, 4, 4, 5, 5,
    4, 4, 4, 4, 4, 5, 5, 6,
    4, 4, 4, 4, 5, 5, 6, 7,
    4, 4, 4, 4, 5, 6, 7, 7,
  ];

  private static readonly byte[] _ExtremeQualityLuma = [
    2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2, 2, 2, 2, 3,
    2, 2, 2, 2, 2, 2, 3, 3,
    2, 2, 2, 2, 2, 3, 3, 3,
    2, 2, 2, 2, 3, 3, 3, 4,
    2, 2, 2, 2, 3, 3, 4, 4,
  ];

  /// <summary>The luma matrix of the frame header, RDD 36:2022, 5.1.1, offsets 20 to 83.</summary>
  private const int _LUMA_MATRIX_AT = 8 + 20;

  /// <summary>The chroma matrix, which follows it.</summary>
  private const int _CHROMA_MATRIX_AT = _LUMA_MATRIX_AT + 64;

  [TestCase("apco", "proxy")]
  [TestCase("apcs", "LT")]
  [TestCase("apcn", "standard")]
  [TestCase("apch", "HQ")]
  [TestCase("ap4h", "4444")]
  [TestCase("ap4x", "4444 XQ")]
  [Category("Unit")]
  public void EveryProfileWritesItsOwnWeightMatricesIntoTheFrameHeader(string codec, string name) {
    var (luma, chroma) = codec switch {
      "apco" => (_ProxyLuma, _ProxyChroma),
      "apcs" => (_Lt, _Lt),
      "apcn" => (_Standard, _Standard),
      "apch" => (_HighQuality, _HighQuality),
      "ap4h" => (_HighQuality, _HighQuality),
      "ap4x" => (_ExtremeQualityLuma, _HighQuality),
      _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };

    var frame = _WriteOneFrame(codec);

    Assert.Multiple(() => {
      Assert.That(frame[_LUMA_MATRIX_AT.._CHROMA_MATRIX_AT], Is.EqualTo(luma).AsCollection,
        $"the luma weights of ProRes {name}");
      Assert.That(frame[_CHROMA_MATRIX_AT..(_CHROMA_MATRIX_AT + 64)], Is.EqualTo(chroma).AsCollection,
        $"the chroma weights of ProRes {name}");
      Assert.That(frame[8 + 19] & 3, Is.EqualTo(3),
        "both load_quant_matrix flags have to be set for the header's matrices to mean anything");
    });
  }

  /// <summary>
  /// The profiles that differ have to keep differing.
  /// </summary>
  /// <remarks>
  /// A writer which quietly served one matrix for every profile passes the per-profile check above
  /// only if the values happen to be the ones it serves. This is the complement: the five distinct
  /// tables Apple's six profiles are written with stay five distinct tables, and the one coincidence
  /// among them — 4444 carrying 422 HQ's weights, which is what FFmpeg's encoder writes too — stays
  /// the only one.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void TheProfilesWhoseMatricesDifferStillDiffer() {
    var written = ProResProfile.All.ToDictionary(
      static profile => profile.Tag.ToString(),
      static profile => _WriteOneFrame(profile.Tag.ToString()));

    var lumaTables = written.ToDictionary(
      static pair => pair.Key,
      static pair => Convert.ToHexString(pair.Value[_LUMA_MATRIX_AT.._CHROMA_MATRIX_AT]));

    Assert.Multiple(() => {
      Assert.That(lumaTables["ap4h"], Is.EqualTo(lumaTables["apch"]), "ProRes 4444 carries 422 HQ's weights");
      Assert.That(lumaTables["ap4x"], Is.Not.EqualTo(lumaTables["apch"]), "4444 XQ has a finer table of its own");
      Assert.That(
        new[] { lumaTables["apco"], lumaTables["apcs"], lumaTables["apcn"], lumaTables["apch"], lumaTables["ap4x"] },
        Is.Unique,
        "the five distinct profile tables must stay distinct");
      Assert.That(
        Convert.ToHexString(written["apco"][_LUMA_MATRIX_AT.._CHROMA_MATRIX_AT]),
        Is.Not.EqualTo(Convert.ToHexString(written["apco"][_CHROMA_MATRIX_AT..(_CHROMA_MATRIX_AT + 64)])),
        "Proxy is the one profile whose chroma table is not its luma table");
    });
  }

  private static byte[] _WriteOneFrame(string codec) {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(codec),
      Width = 32,
      Height = 16,
    };

    var encoder = ProResVideoEncoder.Create(stream);
    var fourFourFour = codec.StartsWith("ap4", StringComparison.Ordinal);
    var picture = new RawImage {
      Width = 32,
      Height = 16,
      Format = fourFourFour ? PixelFormat.Yuv444P12 : PixelFormat.Yuv422P10,
      PixelData = new byte[fourFourFour ? 32 * 16 * 3 * 2 : 32 * 16 * 2 * 2],
    };

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    return packet.Data.ToArray();
  }
}
