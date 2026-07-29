namespace FileFormat.AtariTools800;

/// <summary>Which of the three AtariTools-800 sprite dumps a file is.</summary>
public enum AtariTools800Kind {

  /// <summary>Four players (.4pl): 80x240.</summary>
  Players,

  /// <summary>Four missiles (.4mi): 32x240.</summary>
  Missiles,

  /// <summary>Four players and four missiles side by side (.4pm): 112x240.</summary>
  PlayersAndMissiles,
}
