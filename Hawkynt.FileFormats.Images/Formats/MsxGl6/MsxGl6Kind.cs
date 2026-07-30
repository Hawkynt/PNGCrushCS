namespace FileFormat.MsxGl6;

/// <summary>The two things this layout is used for, which differ only in their default colours.</summary>
public enum MsxGl6Kind {

  /// <summary>A picture, whose colours belong in a companion <c>.PL6</c> file.</summary>
  Picture,

  /// <summary>A Dynamic Publisher stamp, which has no companion and is black on white.</summary>
  Stamp,
}
