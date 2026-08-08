using FileFormat.Wrappers;

namespace FileFormat.WizSolitaireDeck;

/// <summary>Assembles the wrapper: the name it opens with, then the picture.</summary>
public static class WizSolitaireDeckWriter {

  public static byte[] ToBytes(WizSolitaireDeckFile file) => WrappedPicture.Assemble(WizSolitaireDeckFile.Magic, file.Embedded ?? []);
}
