namespace GTI.ModTools.Databases;

public readonly record struct Db10Header(
    string Magic,
    int Count,
    int RecordSize,
    uint Unknown0C,
    uint Unknown14,
    uint Unknown18);

public sealed record Db10Record(
    int Index,
    int Offset,
    int Length,
    IReadOnlyList<string> Strings);

public sealed record Db10Document(
    Db10Header Header,
    IReadOnlyList<Db10Record> Records);
