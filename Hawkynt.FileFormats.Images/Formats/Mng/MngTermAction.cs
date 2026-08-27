namespace FileFormat.Mng;

/// <summary>Action to take when an MNG animation terminates.</summary>
/// <remarks>Values are the wire values from the MNG 1.0 TERM chunk.</remarks>
public enum MngTermAction {
  /// <summary>Show the last frame indefinitely.</summary>
  ShowLast = 0,

  /// <summary>Cease displaying anything.</summary>
  ShowBlank = 1,

  /// <summary>Show the first frame following the TERM chunk.</summary>
  ShowFirst = 2,

  /// <summary>Repeat the sequence between TERM and MEND.</summary>
  Repeat = 3
}
