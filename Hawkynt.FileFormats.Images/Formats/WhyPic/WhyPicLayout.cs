namespace FileFormat.WhyPic;

/// <summary>The two codings WhyPic has written.</summary>
public enum WhyPicLayout {

  /// <summary>Version 1.0: colours are laid along the probability line in index order.</summary>
  Version10,

  /// <summary>
  /// Version 1.1: the colour the longest context predicts is moved to the front of the line, and a
  /// 16-colour picture's indices are renumbered once more by a table stored before the pixels.
  /// </summary>
  Version11,
}
