# OrionGuard v5.0 — Geliştirme Planı

> **Hedef:** Daha performanslı, daha güvenilir, daha kapsamlı bir validation kütüphanesi.
> **Mevcut Versiyon:** 4.0.1 | **Hedef Versiyon:** 5.0.0
> **Target Frameworks:** net8.0, net9.0, net10.0

---

## İçindekiler

1. [Faz 1 — Performans İyileştirmeleri](#faz-1--performans-iyileştirmeleri)
2. [Faz 2 — Güvenilirlik & Test Kapsamı](#faz-2--güvenilirlik--test-kapsamı)
3. [Faz 3 — Yeni Özellikler & Kapsam Genişletme](#faz-3--yeni-özellikler--kapsam-genişletme)
4. [Faz 4 — Middleware & Framework Entegrasyonu](#faz-4--middleware--framework-entegrasyonu)
5. [Faz 5 — Demo Uygulama Yenileme](#faz-5--demo-uygulama-yenileme)
6. [Faz 6 — Dokümantasyon & Release](#faz-6--dokümantasyon--release)

---

## Faz 1 — Performans İyileştirmeleri

### 1.1 Source Generator ile Compile-Time Validation
**Dosya:** `src/Moongazing.OrionGuard.Generators/`

Reflection yerine compile-time code generation kullanarak attribute-based validation'ı sıfır-overhead'e indirmek.

```
📁 src/Moongazing.OrionGuard.Generators/
   ├── OrionGuardGenerator.cs           # IIncrementalGenerator implementasyonu
   ├── ValidationSyntaxReceiver.cs      # Attribute'leri tarayan syntax receiver
   ├── GeneratedValidatorEmitter.cs     # Validator kodu üreten emitter
   └── Moongazing.OrionGuard.Generators.csproj
```

**Yapılacaklar:**
- [ ] `IIncrementalGenerator` ile `[NotNull]`, `[Email]`, `[Range]` vb. attribute'ler için compile-time validator üret
- [ ] `AttributeValidator.Validate(obj)` çağrısını reflection yerine generated koda yönlendir
- [ ] Benchmark: Reflection vs Source Generator karşılaştırması

**Örnek Kullanım (Değişmez — API aynı kalacak):**
```csharp
// Kullanıcı kodu değişmeyecek, arka planda source generator çalışacak
[NotNull, Email]
public string Email { get; set; }

var result = AttributeValidator.Validate(request); // Artık reflection yok
```

---

### 1.2 ObjectValidator Performans — Compiled Expressions
**Dosya:** `src/Moongazing.OrionGuard/Core/ObjectValidator.cs`

Mevcut `ObjectValidator` property erişimi için reflection kullanıyor. Compiled expression tree'ler ile bunu optimize etmek.

**Yapılacaklar:**
- [ ] `Expression.Lambda` ile property accessor'ları compile et ve cache'le
- [ ] `ConcurrentDictionary<Type, CompiledAccessor[]>` ile tip başına bir kez compile
- [ ] İlk çağrıda compile → sonraki çağrılarda delegate invoke (reflection yok)

```csharp
// İç implementasyon değişikliği
internal static class PropertyAccessorCache
{
    private static readonly ConcurrentDictionary<Type, Func<object, object>[]> _cache = new();

    public static Func<object, object>[] GetAccessors(Type type)
    {
        return _cache.GetOrAdd(type, t => CompileAccessors(t));
    }
}
```

---

### 1.3 FastGuard Genişletme — Daha Fazla Span-Based Operasyon
**Dosya:** `src/Moongazing.OrionGuard/Core/FastGuard.cs`

**Yapılacaklar:**
- [ ] `FastGuard.Email(ReadOnlySpan<char>)` — Regex kullanmadan span-based email doğrulama
- [ ] `FastGuard.Url(ReadOnlySpan<char>)` — Span-based URL parsing
- [ ] `FastGuard.Guid(ReadOnlySpan<char>)` — `Guid.TryParse` span overload
- [ ] `FastGuard.NumericString(ReadOnlySpan<char>)` — Sadece rakam kontrolü
- [ ] `FastGuard.AlphaNumeric(ReadOnlySpan<char>)` — Alfanumerik kontrolü
- [ ] `FastGuard.MaxLength(ReadOnlySpan<char>, int)` — Uzunluk kontrolü
- [ ] Tüm yeni metodlara `[MethodImpl(MethodImplOptions.AggressiveInlining)]` ekle

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static ReadOnlySpan<char> Email(ReadOnlySpan<char> value, string parameterName)
{
    // @ pozisyonu bul
    int atIndex = value.IndexOf('@');
    if (atIndex <= 0 || atIndex >= value.Length - 1)
        ThrowHelper.ThrowInvalidEmail(parameterName);

    // Domain kısmında . kontrol et
    var domain = value[(atIndex + 1)..];
    if (domain.IndexOf('.') <= 0)
        ThrowHelper.ThrowInvalidEmail(parameterName);

    return value;
}
```

---

### 1.4 ThrowHelper Pattern
**Dosya:** `src/Moongazing.OrionGuard/Core/ThrowHelper.cs` (YENİ)

Exception throw kodunu ayrı bir static class'a taşıyarak JIT'in happy path'i daha iyi optimize etmesini sağlamak.

**Yapılacaklar:**
- [ ] `ThrowHelper` static class'ı oluştur
- [ ] Tüm `throw new XxxException(...)` çağrılarını `ThrowHelper.ThrowXxx(...)` ile değiştir
- [ ] `[DoesNotReturn]` attribute'ü ile işaretle
- [ ] `[StackTraceHidden]` ile gereksiz stack frame'leri gizle

```csharp
internal static class ThrowHelper
{
    [DoesNotReturn]
    [StackTraceHidden]
    public static void ThrowNullValue(string parameterName)
        => throw new NullValueException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    public static void ThrowInvalidEmail(string parameterName)
        => throw new InvalidEmailException(parameterName);
    // ... tüm exception'lar için
}
```

---

### 1.5 Benchmark Projesi
**Dosya:** `benchmarks/Moongazing.OrionGuard.Benchmarks/`

```
📁 benchmarks/Moongazing.OrionGuard.Benchmarks/
   ├── Moongazing.OrionGuard.Benchmarks.csproj
   ├── Program.cs
   ├── Benchmarks/
   │   ├── FluentGuardBenchmarks.cs
   │   ├── FastGuardBenchmarks.cs
   │   ├── ObjectValidatorBenchmarks.cs
   │   ├── AttributeValidatorBenchmarks.cs
   │   ├── RegexCacheBenchmarks.cs
   │   └── ProfileBenchmarks.cs
   └── Results/
       └── .gitkeep
```

**Yapılacaklar:**
- [ ] BenchmarkDotNet projesi oluştur
- [ ] Her guard tipi için benchmark: FluentGuard vs FastGuard vs Legacy Guard
- [ ] Memory allocation benchmark'ları (allocation-free path'ler için)
- [ ] Regex cache hit/miss performance
- [ ] Source Generator vs Reflection AttributeValidator karşılaştırması
- [ ] Sonuçları `Results/` altında README'ye ekle

---

## Faz 2 — Güvenilirlik & Test Kapsamı

### 2.1 Eksik Test Senaryoları
**Dosya:** `tests/Moongazing.OrionGuard.Tests/`

Mevcut test coverage ciddi boşluklar içeriyor. Hedef: **%95+ line coverage**.

```
📁 tests/Moongazing.OrionGuard.Tests/
   ├── GuardTests.cs                    # Mevcut (güncelle)
   ├── Core/
   │   ├── EnsureTests.cs              # YENİ — Ensure.That() testleri
   │   ├── FluentGuardTests.cs         # YENİ — FluentGuard<T> tüm metodları
   │   ├── AsyncGuardTests.cs          # YENİ — Async validation testleri
   │   ├── GuardResultTests.cs         # YENİ — Result pattern testleri
   │   ├── ObjectValidatorTests.cs     # YENİ — Property validation testleri
   │   ├── ContractTests.cs            # YENİ — Design-by-contract testleri
   │   ├── FastGuardTests.cs           # YENİ — Performans guard'ları
   │   └── LogicalGuardsTests.cs       # YENİ — OR/AND logic testleri
   ├── Extensions/
   │   ├── AdvancedStringGuardsTests.cs  # YENİ — IBAN, CreditCard, TurkishId, JSON, XML, Base64, SemVer, HexColor, Slug
   │   ├── BusinessGuardsTests.cs        # YENİ — MonetaryAmount, Currency, SKU, Coupon, Rating vb.
   │   ├── StringGuardsTests.cs          # YENİ — Tüm string extension'ları
   │   ├── NumericGuardsTests.cs         # YENİ — Tüm numeric extension'ları
   │   ├── DateTimeGuardsTests.cs        # YENİ — Tüm datetime extension'ları
   │   ├── CollectionGuardsTests.cs      # YENİ — Collection extension'ları
   │   ├── NetworkGuardsTests.cs         # YENİ — IP, Port
   │   ├── FileGuardsTests.cs            # YENİ — File validations
   │   └── EnvironmentGuardsTests.cs     # YENİ — Environment variable checks
   ├── Attributes/
   │   └── ValidationAttributeTests.cs   # YENİ — Tüm attribute'ler
   ├── Localization/
   │   └── ValidationMessagesTests.cs    # YENİ — Tüm kültürler, custom mesaj
   ├── Profiles/
   │   ├── CommonProfilesTests.cs        # YENİ — Tüm profiller
   │   └── ProfileRegistryTests.cs       # YENİ — Register/Execute
   ├── DependencyInjection/
   │   └── ServiceCollectionTests.cs     # YENİ — DI entegrasyonu
   └── Integration/
       ├── EndToEndValidationTests.cs    # YENİ — Tam senaryo testleri
       └── ConcurrencyTests.cs           # YENİ — Thread-safety testleri
```

**Yapılacaklar:**
- [ ] Her FluentGuard metodu için: valid input, invalid input, null input, edge case (empty, max, min, boundary)
- [ ] AsyncGuard: başarılı async, başarısız async, exception fırlatan async, cancellation token
- [ ] GuardResult: Combine, Merge, ToErrorDictionary, ThrowIfInvalid, boş result
- [ ] ObjectValidator: tekli property, çoklu property, nested object, null object
- [ ] Contract: Requires başarılı/başarısız, Ensures başarılı/başarısız, Invariant
- [ ] FastGuard: tüm span methodları, null, empty, boundary değerler
- [ ] LogicalGuards: Or (tek true, hepsi false), And (tek false, hepsi true, short-circuit)
- [ ] AdvancedStringGuards: geçerli/geçersiz IBAN, credit card (Luhn), Turkish ID, JSON, XML, Base64
- [ ] BusinessGuards: para birimi, SKU format, iş saatleri, indirim limitleri
- [ ] Localization: EN, TR, DE, FR kültürleri, custom kültür ekleme, fallback davranışı
- [ ] Concurrency: paralel validation çağrıları, regex cache thread-safety

---

### 2.2 Mutation Testing
**Araç:** Stryker.NET

**Yapılacaklar:**
- [ ] `dotnet tool install --global dotnet-stryker` kurulumu
- [ ] `stryker-config.json` konfigürasyonu
- [ ] Hedef mutation score: **%80+**
- [ ] Zayıf testleri güçlendir (mutation kill eden testler yaz)

---

### 2.3 Thread-Safety Audit

**Yapılacaklar:**
- [ ] `RegexCache` → `ConcurrentDictionary` kullanımını doğrula (mevcut ✓, ama stress test yaz)
- [ ] `ValidationMessages` → `SetCulture` thread-safe mi kontrol et, gerekiyorsa `ThreadLocal<CultureInfo>` kullan
- [ ] `GuardProfileRegistry` → concurrent register/execute senaryoları test et
- [ ] `PropertyAccessorCache` (yeni) → thread-safety doğrula
- [ ] `ValidatorFactory` DI lifetime'ları doğrula (Singleton vs Scoped)

---

### 2.4 Exception Hierarchy İyileştirme

**Yapılacaklar:**
- [ ] Tüm exception'lara `ErrorCode` property'si ekle (API response'larda kullanılabilir)
- [ ] `GuardException` base class'a serialization desteği ekle
- [ ] Exception mesajlarını localization sistemine bağla (şu an hardcoded olanlar var)
- [ ] `InnerException` chain'ini düzgün yönet

```csharp
public class GuardException : Exception
{
    public string ErrorCode { get; }
    public string ParameterName { get; }

    public GuardException(string message, string errorCode, string parameterName)
        : base(message)
    {
        ErrorCode = errorCode;
        ParameterName = parameterName;
    }
}
```

---

## Faz 3 — Yeni Özellikler & Kapsam Genişletme

### 3.1 Pipeline Validation (Yeni Pattern)
**Dosya:** `src/Moongazing.OrionGuard/Core/ValidationPipeline.cs` (YENİ)

Sıralı, aşamalı validation pipeline'ı. Her aşama önceki aşamanın sonucuna bağlı.

```csharp
var result = ValidationPipeline.Create<RegisterUserCommand>()
    .Step("input", cmd => Ensure.Accumulate(cmd.Email, "Email").NotNull().Email().ToResult())
    .Step("business", async cmd =>
    {
        var isUnique = await userRepo.IsEmailUniqueAsync(cmd.Email);
        return isUnique
            ? GuardResult.Success()
            : GuardResult.Fail("Email", "Bu email zaten kayıtlı.", "EMAIL_EXISTS");
    })
    .Step("security", cmd =>
    {
        return CommonProfiles.Password(cmd.Password);
    })
    .OnStepFailed((stepName, result) => logger.LogWarning("Validation failed at {Step}", stepName))
    .ExecuteAsync(command);
```

**Yapılacaklar:**
- [ ] `ValidationPipeline<T>` class oluştur
- [ ] `Step()` — sync validation aşaması
- [ ] `StepAsync()` — async validation aşaması
- [ ] `OnStepFailed()` — hata callback
- [ ] `ExecuteAsync()` — pipeline'ı çalıştır, ilk hatada dur veya devam et (configurable)
- [ ] `GuardResult.Success()` ve `GuardResult.Fail()` factory method'ları ekle

---

### 3.2 Cross-Property Validation
**Dosya:** `src/Moongazing.OrionGuard/Core/ObjectValidator.cs` (genişlet)

Birden fazla property arasındaki ilişkiyi doğrulama.

```csharp
var result = Validate.Object(order)
    .Property(o => o.StartDate, g => g.NotNull().InPast())
    .Property(o => o.EndDate, g => g.NotNull().InFuture())
    .CrossProperty(
        o => o.StartDate < o.EndDate,
        "EndDate",
        "Bitiş tarihi başlangıç tarihinden sonra olmalıdır.")
    .CrossProperty(
        o => o.MinQuantity <= o.MaxQuantity,
        "MaxQuantity",
        "Maksimum miktar minimum miktardan büyük olmalıdır.")
    .ToResult();
```

**Yapılacaklar:**
- [ ] `ObjectValidator<T>.CrossProperty(Func<T, bool>, string, string)` metodu ekle
- [ ] `CrossPropertyAsync()` — async versiyonu
- [ ] Cross-property hataları `GuardResult.Errors`'a eklensin

---

### 3.3 Nested Object Validation
**Dosya:** `src/Moongazing.OrionGuard/Core/ObjectValidator.cs` (genişlet)

```csharp
var result = Validate.Object(order)
    .Property(o => o.CustomerName, g => g.NotEmpty())
    .Nested(o => o.ShippingAddress, address =>
    {
        address.Property(a => a.Street, g => g.NotEmpty());
        address.Property(a => a.City, g => g.NotEmpty());
        address.Property(a => a.PostalCode, g => g.Matches(@"^\d{5}$"));
    })
    .Collection(o => o.Items, item =>
    {
        item.Property(i => i.ProductName, g => g.NotEmpty());
        item.Property(i => i.Quantity, g => g.Positive());
        item.Property(i => i.Price, g => g.NotNegative());
    })
    .ToResult();
```

**Yapılacaklar:**
- [ ] `Nested<TProp>(Expression, Action<ObjectValidator<TProp>>)` metodu ekle
- [ ] `Collection<TItem>(Expression, Action<ObjectValidator<TItem>>)` metodu ekle
- [ ] Nested hata mesajlarında parent property adı prefix olarak eklensin: `ShippingAddress.Street`
- [ ] Collection hata mesajlarında index eklensin: `Items[0].ProductName`

---

### 3.4 Yeni Guard Kategorileri

#### 3.4.1 Security Guards (YENİ)
**Dosya:** `src/Moongazing.OrionGuard/Extensions/SecurityGuards.cs`

```csharp
input.AgainstSqlInjection("query");          // SQL injection pattern tespiti
input.AgainstXss("htmlContent");             // XSS pattern tespiti
input.AgainstPathTraversal("filePath");      // ../../ pattern tespiti
input.AgainstCommandInjection("command");    // OS command injection
password.AgainstWeakPassword("password",     // Güçlendirilmiş versiyon
    minLength: 12,
    requireMixedCase: true,
    requireDigit: true,
    requireSpecial: true,
    maxConsecutiveRepeats: 2,
    checkCommonPasswords: true);             // Top 10K yaygın şifre listesi
input.AgainstExcessiveLength("input", 10000); // DoS koruması
```

**Yapılacaklar:**
- [ ] SQL injection pattern'leri: `'; DROP TABLE`, `' OR '1'='1`, `UNION SELECT` vb.
- [ ] XSS pattern'leri: `<script>`, `javascript:`, `onerror=` vb.
- [ ] Path traversal: `../`, `..\\`, `%2e%2e` vb.
- [ ] Command injection: `; rm`, `| cat`, `` `command` `` vb.
- [ ] Common passwords embedded resource olarak (compress + embed)
- [ ] Entropy-based password strength ölçümü

#### 3.4.2 Turkish Domain Guards (YENİ)
**Dosya:** `src/Moongazing.OrionGuard/Extensions/TurkishGuards.cs`

```csharp
// Vergi numarası doğrulama (10 haneli, algoritma kontrolü)
"1234567890".AgainstInvalidTurkishTaxNumber("vergiNo");

// Plaka doğrulama (01-81 il kodu)
"34ABC123".AgainstInvalidTurkishLicensePlate("plaka");

// Posta kodu doğrulama (5 haneli, 01xxx-81xxx)
"34000".AgainstInvalidTurkishPostalCode("postaKodu");

// SGK numarası doğrulama
"1234567890".AgainstInvalidTurkishSgkNumber("sgkNo");

// MERSIS numarası doğrulama (16 haneli)
"0123456789012345".AgainstInvalidMersisNumber("mersisNo");
```

**Yapılacaklar:**
- [ ] Vergi numarası algoritması (Mod 10 tabanlı kontrol)
- [ ] Plaka format regex + il kodu range kontrolü (01-81)
- [ ] Posta kodu format + il kodu uyumu
- [ ] SGK numarası format kontrolü
- [ ] MERSIS numarası format kontrolü (16 hane)

#### 3.4.3 Genel Amaçlı Yeni Guard'lar
**Dosya:** İlgili mevcut extension dosyaları (genişlet)

```csharp
// String — yeni
input.AgainstProfanity("comment", customWordList);    // Küfür filtresi
input.AgainstExactLength("otp", 6);                   // Tam uzunluk
input.AgainstInvalidTimeZone("tz");                    // IANA timezone
input.AgainstInvalidLocale("locale");                  // "en-US", "tr-TR"
input.AgainstInvalidMimeType("contentType");           // "application/json"

// Numeric — yeni
value.AgainstInfinity("calculation");                  // double.IsInfinity
value.AgainstNaN("calculation");                       // double.IsNaN
value.AgainstNotMultipleOf("quantity", 12);            // Çarpan kontrolü

// Collection — yeni
list.AgainstDuplicates("items", x => x.Id);           // Key bazlı duplicate
list.AgainstCountOutOfRange("items", 1, 100);          // Min/max count
dict.AgainstMissingKey("config", "connectionString");  // Dictionary key

// DateTime — yeni  
date.AgainstHoliday("deliveryDate", holidays);         // Tatil günü kontrolü
date.AgainstTooFarInFuture("appointment", TimeSpan.FromDays(365)); // Çok uzak gelecek
timespan.AgainstNegativeDuration("duration");           // Negatif süre
```

---

### 3.5 Localization Genişletme

**Yapılacaklar:**
- [ ] Yeni diller ekle: İspanyolca (ES), Portekizce (PT), Arapça (AR), Japonca (JA)
- [ ] Tüm yeni guard'lar için tüm dillerde mesaj desteği
- [ ] `IStringLocalizer` entegrasyonu (ASP.NET Core ile uyum)
- [ ] Mesaj şablonlarında `{0}`, `{1}` gibi parametre desteğini genişlet
- [ ] Plural form desteği: `"{0} en az {1} karakter olmalıdır"` → `"{0} must be at least {1} character(s)"`

---

### 3.6 Middleware / Interceptor Desteği

**Yapılacaklar:**
- [ ] `IValidationInterceptor` interface ekle
- [ ] Validation öncesi/sonrası hook'lar: `BeforeValidation()`, `AfterValidation()`
- [ ] Logging interceptor (hangi validation'lar çalıştı, ne kadar sürdü)
- [ ] Metrics interceptor (validation success/failure oranları)
- [ ] Telemetry: OpenTelemetry `Activity` entegrasyonu

```csharp
services.AddOrionGuard(config =>
{
    config.AddInterceptor<LoggingValidationInterceptor>();
    config.AddInterceptor<MetricsValidationInterceptor>();
});
```

---

### 3.7 FluentGuard Genişletme — Transform & Map

**Yapılacaklar:**
- [ ] `Transform(Func<T, T>)` — Validation sırasında değeri dönüştür (trim, lowercase vb.)
- [ ] `Map<TResult>(Func<T, TResult>)` — Tip dönüşümü
- [ ] `Default(T defaultValue)` — Null ise default değer ata
- [ ] `Coerce(Func<T, T>)` — Geçersiz değeri düzeltmeyi dene

```csharp
string validEmail = Ensure.That(email)
    .NotNull()
    .Transform(e => e.Trim().ToLowerInvariant())   // Normalize et
    .Email()
    .Value;

int? maybeAge = GetAge();
int validAge = Ensure.That(maybeAge)
    .Default(0)                                     // null ise 0
    .Map(a => a.Value)                              // int? → int
    .InRange(0, 150)
    .Value;
```

---

## Faz 4 — Middleware & Framework Entegrasyonu

### 4.1 ASP.NET Core Middleware
**Dosya:** `src/Moongazing.OrionGuard.AspNetCore/`

```
📁 src/Moongazing.OrionGuard.AspNetCore/
   ├── Moongazing.OrionGuard.AspNetCore.csproj
   ├── OrionGuardMiddleware.cs
   ├── OrionGuardEndpointFilter.cs
   ├── OrionGuardActionFilter.cs
   ├── OrionGuardProblemDetailsFactory.cs
   └── Extensions/
       └── ApplicationBuilderExtensions.cs
```

**Yapılacaklar:**
- [ ] `UseOrionGuardValidation()` middleware — `AggregateValidationException`'ı otomatik yakala, ProblemDetails döndür
- [ ] `[ValidateRequest]` action filter — controller method'larına validation attribute'ü
- [ ] Minimal API endpoint filter — `app.MapPost(...).WithValidation<T>()`
- [ ] ProblemDetails RFC 7807 uyumlu hata response'ları
- [ ] Swagger/OpenAPI validation error schema entegrasyonu

```csharp
// Program.cs
app.UseOrionGuardValidation(options =>
{
    options.IncludeExceptionDetails = app.Environment.IsDevelopment();
    options.UseRfc7807ProblemDetails = true;
});

// Minimal API
app.MapPost("/users", (CreateUserRequest request) => { ... })
   .WithValidation<CreateUserRequest>();

// Controller
[HttpPost]
[ValidateRequest]
public IActionResult Create([FromBody] CreateUserRequest request) { ... }
```

---

### 4.2 MediatR Integration
**Dosya:** `src/Moongazing.OrionGuard.MediatR/`

```
📁 src/Moongazing.OrionGuard.MediatR/
   ├── Moongazing.OrionGuard.MediatR.csproj
   ├── ValidationBehavior.cs
   └── Extensions/
       └── ServiceCollectionExtensions.cs
```

**Yapılacaklar:**
- [ ] `ValidationBehavior<TRequest, TResponse>` pipeline behavior
- [ ] Tüm request'ler için otomatik validation (DI'da `IValidator<T>` kayıtlıysa)
- [ ] `[SkipValidation]` attribute ile bazı request'leri atlama
- [ ] Validation hatasında configurable davranış: throw, return result, vb.

```csharp
services.AddOrionGuardMediatR(typeof(Program).Assembly);
```

---

### 4.3 FluentValidation Migration Helper
**Dosya:** `src/Moongazing.OrionGuard/Migration/`

FluentValidation'dan OrionGuard'a geçişi kolaylaştıran adapter.

**Yapılacaklar:**
- [ ] `AbstractValidator<T>` benzeri base class (zaten var, genişlet)
- [ ] `RuleFor()` syntax'ını destekle (FluentValidation benzeri)
- [ ] Migration guide dökümantasyonu
- [ ] Roslyn analyzer: FluentValidation kullanımlarını algıla, OrionGuard öner

---

## Faz 5 — Demo Uygulama Yenileme

### 5.1 Demo App Yeniden Yapılandırma
**Dosya:** `demo/Moongazing.OrionGuard.Demo/`

Mevcut demo sadece console app. Hedef: **Minimal API + Console** iki modlu demo.

```
📁 demo/Moongazing.OrionGuard.Demo/
   ├── Program.cs                          # Ana giriş (mod seçimi)
   ├── Moongazing.OrionGuard.Demo.csproj
   ├── Dockerfile
   │
   ├── ConsoleDemo/
   │   ├── ConsoleDemoRunner.cs            # Mevcut console demo'yu taşı
   │   ├── PerformanceDemoRunner.cs        # YENİ — FastGuard vs FluentGuard karşılaştırma
   │   ├── SecurityDemoRunner.cs           # YENİ — Security guard örnekleri
   │   └── TurkishDemoRunner.cs            # YENİ — Türkiye'ye özel validation demo
   │
   ├── ApiDemo/
   │   ├── ApiDemoRunner.cs                # YENİ — Minimal API demo
   │   ├── Endpoints/
   │   │   ├── UserEndpoints.cs            # YENİ — /api/users CRUD + validation
   │   │   ├── OrderEndpoints.cs           # YENİ — /api/orders CRUD + validation
   │   │   └── ProductEndpoints.cs         # YENİ — /api/products CRUD + validation
   │   ├── Validators/
   │   │   ├── CreateUserValidator.cs      # YENİ — DI-based validator
   │   │   ├── CreateOrderValidator.cs     # YENİ — Cross-property + nested validation
   │   │   └── CreateProductValidator.cs   # YENİ — Business guard örnekleri
   │   └── Middleware/
   │       └── ValidationExceptionHandler.cs  # YENİ — ProblemDetails middleware
   │
   ├── Models/
   │   ├── UserInput.cs                    # Mevcut (güncelle)
   │   ├── CreateUserRequest.cs            # YENİ — Attribute-based validation
   │   ├── CreateOrderRequest.cs           # YENİ — Nested object (OrderItem, Address)
   │   ├── CreateProductRequest.cs         # YENİ — Business domain model
   │   ├── OrderItem.cs                    # YENİ
   │   ├── Address.cs                      # YENİ
   │   └── ApiErrorResponse.cs             # YENİ — ProblemDetails model
   │
   ├── Services/
   │   ├── RegistrationService.cs          # Mevcut (güncelle)
   │   ├── OrderService.cs                 # YENİ — Pipeline validation örneği
   │   └── ProductService.cs               # YENİ — Business validation örneği
   │
   └── Profiles/
       ├── CustomProfiles.cs               # Mevcut
       ├── TurkishProfiles.cs              # YENİ — TC, Vergi No, Plaka profilleri
       └── ECommerceProfiles.cs            # YENİ — SKU, Coupon, Price profilleri
```

---

### 5.2 Console Demo İçeriği

**ConsoleDemoRunner.cs — Mevcut özellikler (güncelle):**
- [ ] v5.0 API değişikliklerini yansıt
- [ ] Her bölümde başarılı + başarısız örnek göster
- [ ] Renkli console output (`Console.ForegroundColor`)

**PerformanceDemoRunner.cs — Yeni:**
- [ ] FastGuard vs FluentGuard karşılaştırması (Stopwatch ile)
- [ ] Span-based vs string-based validation farkı
- [ ] Memory allocation gösterimi (`GC.GetAllocatedBytesForCurrentThread`)
- [ ] Regex cache performance (ilk çağrı vs sonraki çağrılar)

**SecurityDemoRunner.cs — Yeni:**
- [ ] SQL injection örneği (yakalanan + güvenli input)
- [ ] XSS örneği (yakalanan + güvenli input)
- [ ] Path traversal örneği
- [ ] Güçlü şifre analizi (entropy gösterimi)

**TurkishDemoRunner.cs — Yeni:**
- [ ] TC Kimlik doğrulama (geçerli + geçersiz)
- [ ] Vergi numarası doğrulama
- [ ] Plaka doğrulama
- [ ] IBAN (Türk bankası)
- [ ] Telefon numarası (+90 formatı)
- [ ] Localization TR kültürü ile

---

### 5.3 API Demo İçeriği

**UserEndpoints.cs:**
```csharp
app.MapPost("/api/users", async (CreateUserRequest request, IValidator<CreateUserRequest> validator) =>
{
    var result = await validator.ValidateAsync(request);
    if (result.IsInvalid)
        return Results.ValidationProblem(result.ToErrorDictionary());

    // Simüle kayıt
    return Results.Created($"/api/users/{Guid.NewGuid()}", request);
});
```

**OrderEndpoints.cs — Nested validation showcase:**
```csharp
app.MapPost("/api/orders", (CreateOrderRequest request) =>
{
    var result = Validate.Object(request)
        .Property(o => o.CustomerEmail, g => g.NotEmpty().Email())
        .Nested(o => o.ShippingAddress, addr =>
        {
            addr.Property(a => a.Street, g => g.NotEmpty());
            addr.Property(a => a.City, g => g.NotEmpty());
            addr.Property(a => a.PostalCode, g => g.Matches(@"^\d{5}$"));
        })
        .Collection(o => o.Items, item =>
        {
            item.Property(i => i.ProductName, g => g.NotEmpty());
            item.Property(i => i.Quantity, g => g.Positive());
            item.Property(i => i.UnitPrice, g => g.NotNegative());
        })
        .CrossProperty(o => o.Items.Count > 0, "Items", "Sipariş en az bir ürün içermelidir.")
        .ToResult();

    if (result.IsInvalid)
        return Results.ValidationProblem(result.ToErrorDictionary());

    return Results.Created($"/api/orders/{Guid.NewGuid()}", request);
});
```

**ProductEndpoints.cs — Business guard showcase:**
```csharp
app.MapPost("/api/products", (CreateProductRequest request) =>
{
    var result = GuardResult.Combine(
        Ensure.Accumulate(request.Name, "Name").NotEmpty().MaxLength(200).ToResult(),
        Ensure.Accumulate(request.Sku, "SKU").Must(s => IsValidSku(s), "Geçersiz SKU formatı").ToResult(),
        Ensure.Accumulate(request.Price, "Price").NotNegative().ToResult(),
        Ensure.Accumulate(request.CurrencyCode, "Currency").Must(c => IsValidCurrency(c), "Geçersiz para birimi").ToResult()
    );

    if (result.IsInvalid)
        return Results.ValidationProblem(result.ToErrorDictionary());

    return Results.Created($"/api/products/{Guid.NewGuid()}", request);
});
```

---

### 5.4 Docker & Swagger

**Yapılacaklar:**
- [ ] Dockerfile'ı güncelle (API modu desteği)
- [ ] Swagger UI ekle (`Swashbuckle.AspNetCore`)
- [ ] Swagger'da validation error schema tanımla
- [ ] `docker-compose.yml` ekle (opsiyonel, veritabanı gerektiren test senaryoları için)

---

## Faz 6 — Dokümantasyon & Release

### 6.1 README.md Güncelleme

**Yapılacaklar:**
- [ ] v5.0 badge'leri ekle (NuGet, build status, coverage)
- [ ] Quick Start bölümünü güncelle
- [ ] Performans karşılaştırma tablosu (benchmark sonuçları)
- [ ] Migration Guide: v4.0 → v5.0
- [ ] Yeni feature özetleri
- [ ] API referans linki

---

### 6.2 CHANGELOG.md Güncelleme

```markdown
## [5.0.0] - 2025-XX-XX

### 🚀 Major Features
- Source Generator ile compile-time attribute validation
- Validation Pipeline pattern
- Cross-property & Nested object validation
- Security Guards (SQL injection, XSS, path traversal)
- Turkish Domain Guards (Vergi No, Plaka, Posta Kodu)
- ASP.NET Core middleware & endpoint filter
- MediatR pipeline behavior entegrasyonu
- Transform/Map/Default/Coerce FluentGuard operasyonları
- ThrowHelper pattern ile JIT optimizasyonu

### ⚡ Performance
- ObjectValidator compiled expression cache
- FastGuard span-based Email, URL, Guid, AlphaNumeric
- ThrowHelper pattern (happy path optimization)
- BenchmarkDotNet projesi eklendi

### ✅ Reliability
- %95+ test coverage
- Mutation testing (Stryker.NET) %80+ score
- Thread-safety audit & concurrency testleri
- Exception hierarchy güçlendirildi (ErrorCode, serialization)

### 🌍 Localization
- Yeni diller: ES, PT, AR, JA
- IStringLocalizer entegrasyonu
- Tüm yeni guard'lar için çoklu dil desteği

### 📦 New Packages
- Moongazing.OrionGuard.Generators (Source Generator)
- Moongazing.OrionGuard.AspNetCore (Middleware)
- Moongazing.OrionGuard.MediatR (Pipeline Behavior)
```

---

### 6.3 API Referans Dokümantasyonu

**Yapılacaklar:**
- [ ] DocFX veya xmldoc2md ile otomatik API referansı
- [ ] Her guard kategorisi için kullanım örnekleri
- [ ] GitHub Pages'a deploy

---

### 6.4 NuGet Paket Yapısı

```
📦 NuGet Paketleri:
├── Moongazing.OrionGuard (5.0.0)                    # Core kütüphane
├── Moongazing.OrionGuard.Generators (5.0.0)          # Source generator (compile-time)
├── Moongazing.OrionGuard.AspNetCore (5.0.0)          # ASP.NET Core entegrasyonu
└── Moongazing.OrionGuard.MediatR (5.0.0)             # MediatR entegrasyonu
```

---

## Öncelik Sıralaması & Tahmini Efor

| Faz | Özellik | Öncelik | Efor |
|-----|---------|---------|------|
| 1.4 | ThrowHelper Pattern | 🔴 Kritik | Düşük |
| 1.3 | FastGuard Genişletme | 🔴 Kritik | Orta |
| 2.1 | Eksik Test Senaryoları | 🔴 Kritik | Yüksek |
| 2.3 | Thread-Safety Audit | 🔴 Kritik | Orta |
| 2.4 | Exception Hierarchy | 🟡 Yüksek | Orta |
| 3.2 | Cross-Property Validation | 🟡 Yüksek | Orta |
| 3.3 | Nested Object Validation | 🟡 Yüksek | Orta |
| 3.4.1 | Security Guards | 🟡 Yüksek | Orta |
| 1.1 | Source Generator | 🟡 Yüksek | Yüksek |
| 1.2 | ObjectValidator Compiled Expressions | 🟡 Yüksek | Orta |
| 3.1 | Pipeline Validation | 🟢 Orta | Orta |
| 3.4.2 | Turkish Domain Guards | 🟢 Orta | Düşük |
| 3.4.3 | Yeni Guard Kategorileri | 🟢 Orta | Orta |
| 3.5 | Localization Genişletme | 🟢 Orta | Orta |
| 3.6 | Middleware/Interceptor | 🟢 Orta | Orta |
| 3.7 | Transform & Map | 🟢 Orta | Düşük |
| 4.1 | ASP.NET Core Middleware | 🟢 Orta | Orta |
| 4.2 | MediatR Integration | 🔵 Düşük | Düşük |
| 4.3 | FluentValidation Migration | 🔵 Düşük | Orta |
| 1.5 | Benchmark Projesi | 🔵 Düşük | Düşük |
| 2.2 | Mutation Testing | 🔵 Düşük | Düşük |
| 5.x | Demo App Yenileme | 🟡 Yüksek | Yüksek |
| 6.x | Dokümantasyon & Release | 🟡 Yüksek | Orta |

---

## Teknik Notlar

### Breaking Changes (v5.0)
1. ~~`AttributeValidator.Validate()` artık source-generated kodu kullanır (davranış aynı, performans farklı)~~
   → Sadece source generator paketi ekliyse aktif, yoksa mevcut reflection devam eder (graceful fallback)
2. `GuardException` base class'a `ErrorCode` ve `ParameterName` property'leri eklendi
3. `.NET 10` desteği eklendi (net8.0, net9.0, net10.0)
4. Bazı internal class'lar `sealed` yapıldı (performans)

### Backward Compatibility
- v4.0 API'si tam desteklenir (`Ensure.That`, `Guard.For`, `FastGuard`)
- Legacy extension method'lar kaldırılmaz, sadece `[Obsolete]` annotation ile işaretlenir
- v3.0 `Guard.For()` hala çalışır

### Solution Yapısı (v5.0 Sonrası)
```
Moongazing.OrionGuard.sln
├── src/
│   ├── Moongazing.OrionGuard/                       # Core kütüphane
│   ├── Moongazing.OrionGuard.Generators/            # Source generator
│   ├── Moongazing.OrionGuard.AspNetCore/            # ASP.NET Core middleware
│   └── Moongazing.OrionGuard.MediatR/               # MediatR entegrasyonu
├── tests/
│   ├── Moongazing.OrionGuard.Tests/                 # Unit + Integration testler
│   └── Moongazing.OrionGuard.AspNetCore.Tests/      # Middleware testleri
├── benchmarks/
│   └── Moongazing.OrionGuard.Benchmarks/            # BenchmarkDotNet
├── demo/
│   └── Moongazing.OrionGuard.Demo/                  # Console + API demo
└── docs/
    ├── FEATURES.md
    ├── MIGRATION_v4_to_v5.md
    └── API_REFERENCE.md
```
