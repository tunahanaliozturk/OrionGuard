using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks;

/// <summary>
/// Benchmarks Validate.For&lt;T&gt;() with a simple DTO having 5 properties.
/// Measures the overhead of expression-based property access, fluent chain, and result aggregation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class ObjectValidatorBenchmarks
{
    private SampleDto _validDto = null!;

    [GlobalSetup]
    public void Setup()
    {
        _validDto = new SampleDto
        {
            Name = "Alice",
            Email = "alice@example.com",
            Age = 30,
            Country = "Turkey",
            PhoneNumber = "+905551234567"
        };
    }

    [Benchmark(Baseline = true)]
    public void ManualValidation()
    {
        if (string.IsNullOrWhiteSpace(_validDto.Name))
            throw new ArgumentException("Name is required.");
        if (string.IsNullOrWhiteSpace(_validDto.Email))
            throw new ArgumentException("Email is required.");
        if (_validDto.Age <= 0 || _validDto.Age > 150)
            throw new ArgumentOutOfRangeException(nameof(_validDto.Age));
        if (string.IsNullOrWhiteSpace(_validDto.Country))
            throw new ArgumentException("Country is required.");
        if (string.IsNullOrWhiteSpace(_validDto.PhoneNumber))
            throw new ArgumentException("PhoneNumber is required.");
    }

    [Benchmark]
    public GuardResult Validate_For_AllProperties()
    {
        return Validate.For(_validDto)
            .NotEmpty(d => d.Name)
            .NotEmpty(d => d.Email)
            .Must(d => d.Age, age => age > 0 && age <= 150, "Age must be between 1 and 150.")
            .NotEmpty(d => d.Country)
            .NotEmpty(d => d.PhoneNumber)
            .ToResult();
    }

    [Benchmark]
    public GuardResult Validate_For_WithPropertyChaining()
    {
        return Validate.For(_validDto)
            .Property(d => d.Name, g => g.NotNull().NotEmpty())
            .Property(d => d.Email, g => g.NotNull().NotEmpty().Email())
            .Property(d => d.Age, g => g.Must(a => a > 0, "Age must be positive."))
            .Property(d => d.Country, g => g.NotNull().NotEmpty())
            .Property(d => d.PhoneNumber, g => g.NotNull().NotEmpty())
            .ToResult();
    }

    [Benchmark]
    public void Validate_ForStrict_AllProperties()
    {
        Validate.ForStrict(_validDto)
            .NotEmpty(d => d.Name)
            .NotEmpty(d => d.Email)
            .Must(d => d.Age, age => age > 0 && age <= 150, "Age must be between 1 and 150.")
            .NotEmpty(d => d.Country)
            .NotEmpty(d => d.PhoneNumber)
            .ThrowIfInvalid();
    }

    public class SampleDto
    {
        public string Name { get; set; } = default!;
        public string Email { get; set; } = default!;
        public int Age { get; set; }
        public string Country { get; set; } = default!;
        public string PhoneNumber { get; set; } = default!;
    }
}
