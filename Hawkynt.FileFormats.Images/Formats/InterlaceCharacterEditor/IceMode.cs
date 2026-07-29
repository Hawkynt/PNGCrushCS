namespace FileFormat.InterlaceCharacterEditor;

/// <summary>Which Interlace Character Editor picture format a file is.</summary>
/// <remarks>The values are the mode numbers the editor uses; the extension is what selects one.</remarks>
public enum IceMode {

  /// <summary>Super IRG (.irg): two mode 4 frames sharing one set of registers.</summary>
  SuperIrg = 1,

  /// <summary>Super IRG 2 (.ir2): two mode 4 frames, each with its own playfield registers.</summary>
  SuperIrg2 = 2,

  /// <summary>ICE CIN (.icn): a mode 4 frame blended with a GTIA 11 frame.</summary>
  Cin = 17,

  /// <summary>ICE MIN (.imn): a mode 4 frame blended with a GTIA 9 frame.</summary>
  Min = 18,

  /// <summary>ICE PCIN (.ipc): a mode 4 frame blended with a GTIA 10 frame.</summary>
  Pcin = 19,
}
