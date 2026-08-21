using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// What a stream claims to need of a decoder — ITU-T H.265, clause 7.3.3.
/// </summary>
/// <remarks>
/// <b>The profile is a label, and nothing here is refused on it.</b> That is worth stating because
/// the opposite is the obvious thing to do and it is wrong. An encoder given an all-intra sequence
/// signals the range extensions profile with the intra constraint flag set, even though every tool
/// the stream uses is Main's — so a decoder that refused on the label would refuse the most ordinary
/// still-picture stream there is. What a decoder must actually refuse is announced elsewhere and
/// exactly: the chroma format and the sample depth in the sequence parameter set, and every tool past
/// Main behind its own extension flag in the sequence or picture parameter set. Those are checked
/// where they are read.
/// <para/>
/// What the structure is read for, then, is its length. It sits between the sequence parameter set's
/// first two fields and everything else in it, so a decoder that got its length wrong would read the
/// picture size out of the middle of the constraint flags — and would find a stream that enables
/// coding tools nobody encoded.
/// <para/>
/// The sub-layer structures are stepped over rather than kept. They describe temporal scalability —
/// which sub-layer may be dropped and what a decoder needs for each — and nothing in the sample
/// decoding process consults them.
/// </remarks>
internal sealed class H265ProfileTierLevel {

  /// <summary>Main profile, 8-bit 4:2:0 (Annex A.3.2).</summary>
  internal const int MAIN = 1;

  /// <summary>Main 10 profile (Annex A.3.3).</summary>
  internal const int MAIN10 = 2;

  /// <summary>Main Still Picture profile (Annex A.3.4): one intra picture and nothing else.</summary>
  internal const int MAIN_STILL_PICTURE = 3;

  /// <summary>The format range extensions (Annex A.3.5 onwards).</summary>
  internal const int FORMAT_RANGE_EXTENSIONS = 4;

  private H265ProfileTierLevel(int profileSpace, bool highTier, int profileIdc, uint compatibility, int levelIdc) {
    this.ProfileSpace = profileSpace;
    this.HighTier = highTier;
    this.ProfileIdc = profileIdc;
    this.CompatibilityFlags = compatibility;
    this.LevelIdc = levelIdc;
  }

  internal int ProfileSpace { get; }

  internal bool HighTier { get; }

  internal int ProfileIdc { get; }

  /// <summary>
  /// Which other profiles this stream also conforms to, one bit each.
  /// </summary>
  /// <remarks>
  /// A Main Still Picture stream sets the Main bit too, because a single intra picture is a legal
  /// Main stream; so does every Main stream for itself. Asking this rather than
  /// <see cref="ProfileIdc"/> alone is how a decoder accepts a stream labelled with a profile it does
  /// not know but which the encoder has certified is also readable as one it does.
  /// </remarks>
  internal uint CompatibilityFlags { get; }

  /// <summary>The level, thirty times the number it is written as: 120 is level 4.</summary>
  internal int LevelIdc { get; }

  /// <summary>Whether the stream is readable as Main profile, by its own account.</summary>
  internal bool IsMainCompatible
    => this.ProfileSpace == 0 && (this.ProfileIdc == MAIN || (this.CompatibilityFlags & (1u << MAIN)) != 0);

  /// <summary>The profile as a person would say it, for a refusal message.</summary>
  internal string ProfileName => this.ProfileIdc switch {
    MAIN => "Main",
    MAIN10 => "Main 10",
    MAIN_STILL_PICTURE => "Main Still Picture",
    FORMAT_RANGE_EXTENSIONS => "a format range extension profile",
    5 => "a high throughput profile",
    9 => "a screen content coding profile",
    _ => $"profile {this.ProfileIdc}",
  };

  /// <summary>Reads a <c>profile_tier_level()</c> structure.</summary>
  /// <param name="profilePresent">
  /// Whether the general profile fields are there. Always true where this decoder reads one — the
  /// false case exists only for the layer sets of the multilayer extensions.
  /// </param>
  /// <param name="maxSubLayersMinus1">How many temporal sub-layers the stream declares, less one.</param>
  internal static H265ProfileTierLevel Parse(ref H265BitReader reader, bool profilePresent, int maxSubLayersMinus1) {
    var profileSpace = 0;
    var highTier = false;
    var profileIdc = 0;
    var compatibility = 0u;

    if (profilePresent) {
      profileSpace = reader.ReadBits(2);
      highTier = reader.ReadFlag();
      profileIdc = reader.ReadBits(5);

      for (var i = 0; i < 32; ++i)
        compatibility |= (uint)reader.ReadBit() << i;

      // The forty-eight bits that follow are the four source-scan and packing flags, then a block
      // the first version of the standard reserved and the extensions have been spending since on
      // constraints — which sample depths, which chroma formats and whether the stream is all intra.
      // None of them changes a sample: they narrow what a conforming encoder was allowed to do, and
      // what a decoder must actually refuse is announced separately by the extension flags of the
      // sequence and picture parameter sets. The whole structure is eighty-eight bits before the
      // level, and getting its length wrong puts every field of the sequence parameter set after it
      // out of place — which reads as a stream that enables tools it does not use.
      reader.Skip(48);
    }

    var levelIdc = reader.ReadBits(8);

    var profileFlags = new bool[maxSubLayersMinus1];
    var levelFlags = new bool[maxSubLayersMinus1];
    for (var i = 0; i < maxSubLayersMinus1; ++i) {
      profileFlags[i] = reader.ReadFlag();
      levelFlags[i] = reader.ReadFlag();
    }

    // Eight sub-layers' worth of flags are always present when there is more than one, padded with
    // reserved pairs — so the structure's length does not depend on how many are actually declared.
    if (maxSubLayersMinus1 > 0)
      reader.Skip((8 - maxSubLayersMinus1) * 2);

    for (var i = 0; i < maxSubLayersMinus1; ++i) {
      if (profileFlags[i])
        reader.Skip(88);

      if (levelFlags[i])
        reader.Skip(8);
    }

    return new(profileSpace, highTier, profileIdc, compatibility, levelIdc);
  }
}
