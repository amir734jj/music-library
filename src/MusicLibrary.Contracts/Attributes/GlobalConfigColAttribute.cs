namespace MusicLibrary.Contracts.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class GlobalConfigColAttribute : Attribute
{
    public required string Name { get; init; }
}