using System.Collections.Generic;

namespace FileFormat.Codecs.DnxHd;

/// <summary>The fixed geometry and coding-unit size of one classic DNxHD compression identifier.</summary>
/// <remarks>
/// These are the non-variable rows of SMPTE ST 2019-1:2016 Annex C. DNxHR identifiers 1270-1274
/// deliberately are not represented here: their frame size is computed from the raster rather than
/// fixed by the identifier.
/// </remarks>
internal sealed class DnxHdProfile {

  internal required int CompressionId { get; init; }
  internal required int Width { get; init; }
  internal required int Height { get; init; }
  internal required int BitDepth { get; init; }
  internal required int FrameSize { get; init; }
  internal required int CodingUnitSize { get; init; }
  internal required bool Interlaced { get; init; }
  internal required bool AdaptiveMacroblocks { get; init; }
  internal required bool Is444 { get; init; }
  internal required int HeaderVersion { get; init; }

  private static readonly Dictionary<int, DnxHdProfile> _ById = _Build();

  private static Dictionary<int, DnxHdProfile> _Build() {
    var result = new Dictionary<int, DnxHdProfile>();

    _Add(result, 1235, 1920, 1080, 10, 917_504, 917_504);
    _Add(result, 1237, 1920, 1080, 8, 606_208, 606_208);
    _Add(result, 1238, 1920, 1080, 8, 917_504, 917_504);
    _Add(result, 1241, 1920, 1080, 10, 917_504, 458_752, interlaced: true);
    _Add(result, 1242, 1920, 1080, 8, 606_208, 303_104, interlaced: true);
    _Add(result, 1243, 1920, 1080, 8, 917_504, 458_752, interlaced: true);
    _Add(result, 1244, 1440, 1080, 8, 606_208, 303_104, interlaced: true);
    _Add(result, 1250, 1280, 720, 10, 458_752, 458_752);
    _Add(result, 1251, 1280, 720, 8, 458_752, 458_752);
    _Add(result, 1252, 1280, 720, 8, 303_104, 303_104);
    _Add(result, 1253, 1920, 1080, 8, 188_416, 188_416);
    _Add(result, 1256, 1920, 1080, 10, 1_835_008, 1_835_008, is444: true, headerVersion: 2);
    _Add(result, 1258, 960, 720, 8, 212_992, 212_992, headerVersion: 2);
    _Add(result, 1259, 1440, 1080, 8, 417_792, 417_792, headerVersion: 2);
    _Add(result, 1260, 1440, 1080, 8, 835_584, 417_792, interlaced: true, adaptive: true, headerVersion: 2);

    return result;
  }

  private static void _Add(
    Dictionary<int, DnxHdProfile> target,
    int id, int width, int height, int depth, int frameSize, int codingUnitSize,
    bool interlaced = false, bool adaptive = false, bool is444 = false, int headerVersion = 1)
    => target.Add(id, new() {
      CompressionId = id,
      Width = width,
      Height = height,
      BitDepth = depth,
      FrameSize = frameSize,
      CodingUnitSize = codingUnitSize,
      Interlaced = interlaced,
      AdaptiveMacroblocks = adaptive,
      Is444 = is444,
      HeaderVersion = headerVersion,
    });

  internal static DnxHdProfile? Find(int compressionId) => _ById.GetValueOrDefault(compressionId);

  /// <summary>
  /// Selects this library's default progressive 8-bit 4:2:2 profile for a standard DNxHD raster.
  /// </summary>
  /// <remarks>
  /// Where Annex C offers more than one bitrate at a raster, the larger fixed frame is selected.
  /// The stream model has no bitrate/quality request to distinguish the rows, so silently choosing
  /// the smaller one would throw quality away for no stated reason.
  /// </remarks>
  internal static DnxHdProfile? SelectProgressive8Bit422(int width, int height)
    => (width, height) switch {
      (1920, 1080) => Find(1238),
      (1440, 1080) => Find(1259),
      (1280, 720) => Find(1251),
      (960, 720) => Find(1258),
      _ => null,
    };
}
