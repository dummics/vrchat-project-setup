namespace VrcSetup.Core;

public sealed class ImportQueueInUseException(Exception inner) : IOException("Another instance is already managing imports. Return to that window or close it before continuing.", inner);
