using System.Globalization;
using Moongazing.OrionGuard.Localization;

namespace Moongazing.OrionGuard.Tests;

public class ValidationMessagesTests
{
    #region Culture Management

    [Fact]
    public void SetCulture_ShouldChangeCulture()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture(new CultureInfo("tr"));
            Assert.Equal("tr", ValidationMessages.CurrentCulture.TwoLetterISOLanguageName);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void SetCulture_ByName_ShouldChangeCulture()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("de");
            Assert.Equal("de", ValidationMessages.CurrentCulture.TwoLetterISOLanguageName);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void SetCulture_ShouldThrow_WhenNull()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationMessages.SetCulture((CultureInfo)null!));
    }

    [Fact]
    public void SetCulture_ByName_ShouldThrow_WhenNull()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationMessages.SetCulture((string)null!));
    }

    #endregion

    #region Scoped Culture (AsyncLocal)

    [Fact]
    public async Task SetCultureForCurrentScope_ShouldOnlyAffectCurrentContext()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("en");

            string? scopedCulture = null;
            string? outerCulture = null;

            await Task.Run(() =>
            {
                ValidationMessages.SetCultureForCurrentScope(new CultureInfo("tr"));
                scopedCulture = ValidationMessages.CurrentCulture.TwoLetterISOLanguageName;
            });

            outerCulture = ValidationMessages.CurrentCulture.TwoLetterISOLanguageName;

            Assert.Equal("tr", scopedCulture);
            Assert.Equal("en", outerCulture);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    #endregion

    #region Get Messages

    [Fact]
    public void Get_ShouldReturnEnglishMessage_WhenCultureIsEnglish()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("en");
            var message = ValidationMessages.Get("NotNull", "Email");
            Assert.Equal("Email cannot be null.", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_ShouldReturnTurkishMessage_WhenCultureIsTurkish()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("tr");
            var message = ValidationMessages.Get("NotNull", "Email");
            Assert.Equal("Email boş olamaz.", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_ShouldReturnGermanMessage_WhenCultureIsGerman()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("de");
            var message = ValidationMessages.Get("NotNull", "Email");
            Assert.Equal("Email darf nicht null sein.", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_ShouldReturnFrenchMessage_WhenCultureIsFrench()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("fr");
            var message = ValidationMessages.Get("NotNull", "Email");
            Assert.Equal("Email ne peut pas être null.", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_ShouldFallbackToEnglish_WhenCultureNotSupported()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("zh"); // Chinese - not in the dictionary
            var message = ValidationMessages.Get("NotNull", "Email");
            Assert.Equal("Email cannot be null.", message); // Falls back to English
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_ShouldReturnKey_WhenMessageNotFound()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("en");
            var message = ValidationMessages.Get("NonExistentKey");
            Assert.Equal("NonExistentKey", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_ShouldFormatMessageWithArgs()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("en");
            var message = ValidationMessages.Get("InRange", "Age", 1, 100);
            Assert.Equal("Age must be between 1 and 100.", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void Get_WithExplicitCulture_ShouldUseSpecifiedCulture()
    {
        var message = ValidationMessages.Get("NotNull", new CultureInfo("tr"), "Email");
        Assert.Equal("Email boş olamaz.", message);
    }

    #endregion

    #region AddMessages

    [Fact]
    public void AddMessages_ShouldAddCustomMessages()
    {
        ValidationMessages.AddMessages("en", new Dictionary<string, string>
        {
            ["CustomKey"] = "Custom message for {0}."
        });

        var original = ValidationMessages.CurrentCulture;
        try
        {
            ValidationMessages.SetCulture("en");
            var message = ValidationMessages.Get("CustomKey", "TestParam");
            Assert.Equal("Custom message for TestParam.", message);
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    [Fact]
    public void AddMessages_ShouldOverrideExistingMessages()
    {
        var original = ValidationMessages.CurrentCulture;
        try
        {
            var originalMessage = ValidationMessages.Get("NotNull", new CultureInfo("en"), "X");

            ValidationMessages.AddMessages("en", new Dictionary<string, string>
            {
                ["NotNull"] = "OVERRIDDEN: {0} is null!"
            });

            ValidationMessages.SetCulture("en");
            var newMessage = ValidationMessages.Get("NotNull", "X");
            Assert.Equal("OVERRIDDEN: X is null!", newMessage);

            // Restore original
            ValidationMessages.AddMessages("en", new Dictionary<string, string>
            {
                ["NotNull"] = "{0} cannot be null."
            });
        }
        finally
        {
            ValidationMessages.SetCulture(original);
        }
    }

    #endregion

    #region SetMessageResolver

    [Fact]
    public void SetMessageResolver_ShouldThrow_WhenNull()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationMessages.SetMessageResolver(null!));
    }

    #endregion

    #region Supported Languages

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("ar")]
    [InlineData("ja")]
    public void Get_ShouldReturnMessage_ForAllSupportedLanguages(string cultureName)
    {
        var message = ValidationMessages.Get("NotNull", new CultureInfo(cultureName), "Field");
        Assert.NotEqual("NotNull", message); // Should NOT fall back to key
        Assert.Contains("Field", message);   // Should contain the formatted parameter
    }

    #endregion
}
