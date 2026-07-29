namespace FileFormat.MadStudio;

/// <summary>Which ANTIC character mode a Mad Studio screen was drawn in.</summary>
public enum MadStudioMode {

  /// <summary>ANTIC 2 (.an2): 40x24 cells of 8x8, two colours, no stored palette.</summary>
  Antic2,

  /// <summary>ANTIC 4 (.an4): 40x24 cells of 8x8, five colour registers.</summary>
  Antic4,

  /// <summary>ANTIC 5 (.an5): 40x12 cells of 8x16 — ANTIC 4 with every glyph row drawn twice.</summary>
  Antic5,

  /// <summary>Graphics 1 (.gr1): 20x24 cells of 16x8, the character code choosing among four registers.</summary>
  Graphics1,

  /// <summary>Graphics 2 (.gr2): 20x12 cells of 16x16 — Graphics 1 with every glyph row drawn twice.</summary>
  Graphics2,
}
