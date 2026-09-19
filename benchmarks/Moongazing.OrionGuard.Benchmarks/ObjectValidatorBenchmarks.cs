using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

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

/// <summary>
/// Covers the validation paths a request handler hits on every call: the nested/collection validator,
/// the object validator on both a passing and a failing instance, the strict (throwing) validator,
/// <see cref="AbstractValidator{T}"/>, and the FluentValidation compatibility layer.
/// </summary>
[MemoryDiagnoser]
public class HotPathBenchmarks
{
    private Order _validOrder = null!;
    private Order _invalidOrder = null!;
    private ObjectValidatorBenchmarks.SampleDto _validDto = null!;
    private ObjectValidatorBenchmarks.SampleDto _invalidDto = null!;
    private OrderValidator _abstractValidator = null!;
    private CompatOrderValidator _compatValidator = null!;
    private GuardResult _failedResult = null!;

    [GlobalSetup]
    public void Setup()
    {
        _validOrder = NewOrder("ORD-1", "alice@example.com", itemCount: 10);
        _invalidOrder = NewOrder("", "not-an-email", itemCount: 10);
        _invalidOrder.Items[4].Quantity = 0;

        _validDto = new ObjectValidatorBenchmarks.SampleDto
        {
            Name = "Alice",
            Email = "alice@example.com",
            Age = 30,
            Country = "Turkey",
            PhoneNumber = "+905551234567"
        };
        _invalidDto = new ObjectValidatorBenchmarks.SampleDto
        {
            Name = "",
            Email = "",
            Age = 0,
            Country = "",
            PhoneNumber = ""
        };

        _abstractValidator = new OrderValidator();
        _compatValidator = new CompatOrderValidator();
        _failedResult = GuardResult.Failure(
        [
            new ValidationError("Number", "required"),
            new ValidationError("Email", "invalid"),
        ]);
    }

    private static Order NewOrder(string number, string email, int itemCount)
    {
        var order = new Order
        {
            Number = number,
            Total = 250m,
            Customer = new Customer { Email = email, Address = new Address { City = "Ankara", ZipCode = "06100" } },
            Items = []
        };

        for (var i = 0; i < itemCount; i++)
        {
            order.Items.Add(new OrderLine { ProductName = $"Product {i}", Quantity = i + 1 });
        }

        return order;
    }

    [Benchmark]
    public GuardResult Nested_ValidOrder() => ValidateNested(_validOrder);

    [Benchmark]
    public GuardResult Nested_InvalidOrder() => ValidateNested(_invalidOrder);

    private static GuardResult ValidateNested(Order order) =>
        Validate.Nested(order)
            .Property(o => o.Number, p => p.NotEmpty())
            .Nested(o => o.Customer, customer => customer
                .Property(c => c.Email, p => p.Email())
                .Nested(c => c.Address, address => address
                    .Property(a => a.City, p => p.NotEmpty())
                    .Property(a => a.ZipCode, p => p.Length(5, 10))))
            .Collection(o => o.Items, (item, _) => item
                .Property(i => i.ProductName, p => p.NotEmpty())
                .Property(i => i.Quantity, p => p.GreaterThan(0)))
            .ToResult();

    [Benchmark]
    public GuardResult ObjectValidator_Valid() => ValidateDto(Validate.For(_validDto));

    [Benchmark]
    public GuardResult ObjectValidator_Invalid() => ValidateDto(Validate.For(_invalidDto));

    private static GuardResult ValidateDto(ObjectValidator<ObjectValidatorBenchmarks.SampleDto> validator) =>
        validator
            .Property(d => d.Name, g => g.NotNull().NotEmpty())
            .Property(d => d.Email, g => g.NotNull().NotEmpty())
            .Property(d => d.Age, g => g.Must(a => a > 0, "Age must be positive."))
            .Property(d => d.Country, g => g.NotNull().NotEmpty())
            .Property(d => d.PhoneNumber, g => g.NotNull().NotEmpty())
            .ToResult();

    /// <summary>
    /// The same rules as <see cref="ObjectValidator_Valid"/> through the delegate overload, which skips the
    /// expression tree the compiler would otherwise rebuild per property per call.
    /// </summary>
    [Benchmark]
    public GuardResult ObjectValidator_Valid_DelegateSelector() =>
        Validate.For(_validDto)
            .Property(static d => d.Name, "Name", g => g.NotNull().NotEmpty())
            .Property(static d => d.Email, "Email", g => g.NotNull().NotEmpty())
            .Property(static d => d.Age, "Age", g => g.Must(a => a > 0, "Age must be positive."))
            .Property(static d => d.Country, "Country", g => g.NotNull().NotEmpty())
            .Property(static d => d.PhoneNumber, "PhoneNumber", g => g.NotNull().NotEmpty())
            .ToResult();

    /// <summary>
    /// The strict validator throws on the first failure, so the caller that wants a result instead of an
    /// exception pays for the throw. Measures that whole round trip.
    /// </summary>
    [Benchmark]
    public int ObjectValidator_Strict_Invalid()
    {
        try
        {
            Validate.ForStrict(_invalidDto)
                .Property(d => d.Name, g => g.NotNull().NotEmpty())
                .Property(d => d.Email, g => g.NotNull().NotEmpty())
                .ThrowIfInvalid();
            return 0;
        }
        catch (AggregateValidationException ex)
        {
            return ex.Errors.Count;
        }
    }

    [Benchmark]
    public GuardResult AbstractValidator_Valid() => _abstractValidator.Validate(_validOrder);

    [Benchmark]
    public GuardResult AbstractValidator_Invalid() => _abstractValidator.Validate(_invalidOrder);

    [Benchmark]
    public GuardResult Compat_Valid() => _compatValidator.Validate(_validOrder);

    /// <summary>
    /// <see cref="GuardResult.Errors"/> is read by every caller that inspects a failure, and by
    /// <see cref="GuardResult.ThrowIfInvalid"/>.
    /// </summary>
    [Benchmark]
    public int GuardResult_ReadErrors() => _failedResult.Errors.Count + _failedResult.Errors.Count;

    [Benchmark]
    public GuardResult GuardResult_Success() => GuardResult.Success();

    public sealed class OrderValidator : AbstractValidator<Order>
    {
        public OrderValidator()
        {
            RuleFor(o => o.Number, nameof(Order.Number), p => p.NotEmpty());
            RuleFor(o => o.Customer.Email, "Email", p => p.Email());
            RuleFor(o => o.Total > 0, "Total must be positive.", nameof(Order.Total));
        }
    }

    public sealed class CompatOrderValidator : Compatibility.FluentStyleValidator<Order>
    {
        public CompatOrderValidator()
        {
            RuleFor(o => o.Number).NotEmpty();
            RuleFor(o => o.Total).InclusiveBetween(1m, 10_000m);
            RuleFor(o => o.Items.Count).GreaterThan(0);
        }
    }

    public sealed class Order
    {
        public string Number { get; set; } = default!;
        public decimal Total { get; set; }
        public Customer Customer { get; set; } = default!;
        public List<OrderLine> Items { get; set; } = default!;
    }

    public sealed class Customer
    {
        public string Email { get; set; } = default!;
        public Address Address { get; set; } = default!;
    }

    public sealed class Address
    {
        public string City { get; set; } = default!;
        public string ZipCode { get; set; } = default!;
    }

    public sealed class OrderLine
    {
        public string ProductName { get; set; } = default!;
        public int Quantity { get; set; }
    }
}
