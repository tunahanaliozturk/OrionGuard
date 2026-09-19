using System.Text.Json.Serialization;
using Moongazing.OrionGuard.Attributes;

namespace Moongazing.OrionGuard.SchemaExport.Tests;

/// <summary>A type with no OrionGuard rules at all: only its shape can be exported.</summary>
public sealed class PlainProfile
{
    public string DisplayName { get; set; } = string.Empty;
    public int Visits { get; set; }
}

/// <summary>One member per constraint the exporters can express.</summary>
public sealed class ConstrainedRequest
{
    [NotNull] public string Id { get; set; } = string.Empty;
    [NotEmpty] public string Nickname { get; set; } = string.Empty;
    [Length(3, 50)] public string Slug { get; set; } = string.Empty;
    [Regex("^[a-z0-9-]+$")] public string Handle { get; set; } = string.Empty;
    [Email] public string Email { get; set; } = string.Empty;
    [Range(13, 120)] public int Age { get; set; }
    [Positive] public decimal Budget { get; set; }
    public Uri? Website { get; set; }
}

public sealed class Address
{
    [NotEmpty] public string City { get; set; } = string.Empty;
}

public sealed class Order
{
    [NotNull] public Address ShipTo { get; set; } = new();
    public Address? BillTo { get; set; }
}

public sealed class OrderLine
{
    [Positive] public int Quantity { get; set; }
}

public sealed class Basket
{
    [NotEmpty] public List<OrderLine> Lines { get; set; } = [];
    public string[] Tags { get; set; } = [];
}

public sealed class Contact
{
    [NotNull] public string Email { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public int? Age { get; set; }
}

public enum ShipmentStatus
{
    Pending,
    Shipped,
    Delivered
}

public sealed class Shipment
{
    public ShipmentStatus Status { get; set; }
    public ShipmentStatus? PreviousStatus { get; set; }
}

/// <summary>A rule that compares two properties, which no artifact keyword can express.</summary>
public sealed class MatchesPasswordAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => true;

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} must match Password.";
}

/// <summary>A custom predicate: OrionGuard runs it, the exporter cannot read it.</summary>
public sealed class EvenNumberAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is int number && number % 2 == 0;

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} must be even.";
}

/// <summary>Serialized names that are not valid TypeScript identifiers.</summary>
public sealed class WireNames
{
    [JsonPropertyName("plain")] public string Plain { get; set; } = string.Empty;
    [JsonPropertyName("first-name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("2fa")] public bool TwoFactorEnabled { get; set; }
    [JsonPropertyName("say \"hi\"")] public string? Greeting { get; set; }
}

/// <summary>Patterns an ECMAScript engine can and cannot read the way .NET does.</summary>
public sealed class PatternRules
{
    [Regex("^[a-z]+$")] public string Portable { get; set; } = string.Empty;
    [Regex("(?i)^abc$")] public string InlineOptions { get; set; } = string.Empty;
    [Regex(@"\Aabc\z")] public string DotNetAnchors { get; set; } = string.Empty;
    [Regex(@"(?<word>\w+)")] public string NamedGroup { get; set; } = string.Empty;
    [Regex(@"\p{Lu}")] public string UnicodeCategory { get; set; } = string.Empty;
    [Regex("[a-z-[aeiou]]")] public string ClassSubtraction { get; set; } = string.Empty;
    [Regex("(?=.*[0-9])^.{4,}$")] public string Lookahead { get; set; } = string.Empty;

    [NotEmpty]
    [Regex("^[a-z]+$")]
    public string NotBlankAndPattern { get; set; } = string.Empty;
}

public sealed class Roster
{
    public List<string?> Nicknames { get; set; } = [];
    public List<string> Names { get; set; } = [];
    public string?[] Aliases { get; set; } = [];
    public List<int?> Scores { get; set; } = [];
}

public sealed class Warehouse
{
    public List<List<Address>> Shelves { get; set; } = [];
}

public sealed class Upload
{
    public byte[] Content { get; set; } = [];
    public byte[]? Thumbnail { get; set; }
}

public sealed class PasswordChange
{
    [NotNull]
    [Length(8, 64)]
    public string Password { get; set; } = string.Empty;

    [MatchesPassword] public string Confirmation { get; set; } = string.Empty;

    [EvenNumber] public int Slots { get; set; }
}
