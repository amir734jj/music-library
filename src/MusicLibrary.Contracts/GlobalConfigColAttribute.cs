namespace MusicLibrary.Contracts;

[AttributeUsage(AttributeTargets.Property)]
public sealed class GlobalConfigColAttribute : Attribute
{
    public required string Name { get; init; }
}