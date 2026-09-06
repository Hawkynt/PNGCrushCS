using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// The reconstructed luma and colour-difference planes of one frame, at eight bits.
/// </summary>
/// <remarks>
/// Planes rather than packed pixels, because this is where DV's decoding process actually ends and it
/// is what a comparison against another decoder has to be made on. Everything after — choosing a
/// matrix, resampling the colour up to every luma column, packing to RGB — is a display convention
/// that two correct decoders disagree about, and comparing packed colour measures that disagreement
/// instead of the decode.
/// <para/>
/// The plane sizes are the profile's and are always whole: every standard-definition DV raster is a
/// whole number of macroblocks in both directions, so there is no padding to allocate and none to
/// crop away.
/// </remarks>
internal sealed class DvPlanes {

  internal required int Width { get; init; }
  internal required int Height { get; init; }
  internal required int ChromaWidth { get; init; }
  internal required int ChromaHeight { get; init; }

  internal required byte[] Luma { get; init; }
  internal required byte[] Cb { get; init; }
  internal required byte[] Cr { get; init; }

  internal static DvPlanes Allocate(DvProfile profile) {
    ArgumentNullException.ThrowIfNull(profile);

    return new() {
      Width = profile.Width,
      Height = profile.Height,
      ChromaWidth = profile.ChromaWidth,
      ChromaHeight = profile.ChromaHeight,
      Luma = new byte[profile.Width * profile.Height],
      Cb = new byte[profile.ChromaWidth * profile.ChromaHeight],
      Cr = new byte[profile.ChromaWidth * profile.ChromaHeight],
    };
  }
}
