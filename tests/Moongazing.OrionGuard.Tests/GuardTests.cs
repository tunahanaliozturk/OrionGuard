using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Moongazing.OrionGuard.Tests
{
    public class GuardTests
    {
        [Fact]
        public void AgainstNull_ShouldThrowNullValueException_WhenValueIsNull()
        {
            // Arrange
            object testObject = null;

            // Act & Assert
            Assert.Throws<NullValueException>(() => Guard.AgainstNull(testObject, nameof(testObject)));
        }

        [Fact]
        public void AgainstNullOrEmpty_ShouldThrowEmptyStringException_WhenStringIsNullOrWhitespace()
        {
            // Arrange
            string testString = " ";

            // Act & Assert
            Assert.Throws<EmptyStringException>(() => Guard.AgainstNullOrEmpty(testString, nameof(testString)));
        }

        [Fact]
        public void AgainstOutOfRange_ShouldThrowOutOfRangeException_WhenValueIsOutOfRange()
        {
            // Arrange
            int value = 15;

            // Act & Assert
            Assert.Throws<OutOfRangeException>(() => Guard.AgainstOutOfRange(value, 5, 10, nameof(value)));
        }

        [Fact]
        public void AgainstNegative_ShouldThrowNegativeException_WhenValueIsNegative()
        {
            // Arrange
            int value = -1;

            // Act & Assert
            Assert.Throws<NegativeException>(() => Guard.AgainstNegative(value, nameof(value)));
        }

        [Fact]
        public void AgainstNegativeDecimal_ShouldThrowNegativeDecimalException_WhenValueIsNegative()
        {
            // Arrange
            decimal value = -1.5m;

            // Act & Assert
            Assert.Throws<NegativeDecimalException>(() => Guard.AgainstNegativeDecimal(value, nameof(value)));
        }

        [Fact]
        public void AgainstLessThan_ShouldThrowLessThanException_WhenValueIsLessThanMinimum()
        {
            // Arrange
            int value = 3;

            // Act & Assert
            Assert.Throws<LessThanException>(() => Guard.AgainstLessThan(value, 5, nameof(value)));
        }

        [Fact]
        public void AgainstGreaterThan_ShouldThrowGreaterThanException_WhenValueIsGreaterThanMaximum()
        {
            // Arrange
            int value = 20;

            // Act & Assert
            Assert.Throws<GreaterThanException>(() => Guard.AgainstGreaterThan(value, 10, nameof(value)));
        }

        [Fact]
        public void AgainstFalse_ShouldThrowFalseException_WhenValueIsFalse()
        {
            // Arrange
            bool value = false;

            // Act & Assert
            Assert.Throws<FalseException>(() => Guard.AgainstFalse(value, nameof(value)));
        }

        [Fact]
        public void AgainstTrue_ShouldThrowTrueException_WhenValueIsTrue()
        {
            // Arrange
            bool value = true;

            // Act & Assert
            Assert.Throws<TrueException>(() => Guard.AgainstTrue(value, nameof(value)));
        }

        [Fact]
        public void AgainstUninitializedProperties_ShouldThrowUninitializedPropertyException_WhenObjectHasUninitializedProperties()
        {
            // Arrange
            var obj = new TestObject { Name = "John", Age = null };

            // Act & Assert
            Assert.Throws<UninitializedPropertyException>(() => Guard.AgainstUninitializedProperties(obj, nameof(obj)));
        }

        [Fact]
        public void AgainstInvalidEmail_ShouldThrowInvalidEmailException_WhenEmailIsInvalid()
        {
            // Arrange
            string invalidEmail = "invalid-email";

            // Act & Assert
            Assert.Throws<InvalidEmailException>(() => Guard.AgainstInvalidEmail(invalidEmail, nameof(invalidEmail)));
        }

        [Fact]
        public void AgainstInvalidUrl_ShouldThrowInvalidUrlException_WhenUrlIsInvalid()
        {
            // Arrange
            string invalidUrl = "not-a-url";

            // Act & Assert
            Assert.Throws<InvalidUrlException>(() => Guard.AgainstInvalidUrl(invalidUrl, nameof(invalidUrl)));
        }

        [Fact]
        public void AgainstInvalidIp_ShouldThrowInvalidIpException_WhenIpAddressIsInvalid()
        {
            // Arrange
            string invalidIp = "999.999.999.999";

            // Act & Assert
            Assert.Throws<InvalidIpException>(() => Guard.AgainstInvalidIp(invalidIp, nameof(invalidIp)));
        }

        [Fact]
        public void AgainstInvalidGuid_ShouldThrowInvalidGuidException_WhenGuidIsInvalid()
        {
            // Arrange
            string invalidGuid = "not-a-guid";

            // Act & Assert
            Assert.Throws<InvalidGuidException>(() => Guard.AgainstInvalidGuid(invalidGuid, nameof(invalidGuid)));
        }

        [Fact]
        public void AgainstPastDate_ShouldThrowPastDateException_WhenDateIsInPast()
        {
            // Arrange
            DateTime pastDate = DateTime.Now.AddDays(-1);

            // Act & Assert
            Assert.Throws<PastDateException>(() => Guard.AgainstPastDate(pastDate, nameof(pastDate)));
        }

        [Fact]
        public void AgainstFutureDate_ShouldThrowFutureDateException_WhenDateIsInFuture()
        {
            // Arrange
            DateTime futureDate = DateTime.Now.AddDays(1);

            // Act & Assert
            Assert.Throws<FutureDateException>(() => Guard.AgainstFutureDate(futureDate, nameof(futureDate)));
        }

        [Fact]
        public void AgainstEmptyFile_ShouldThrowEmptyFileException_WhenFileIsEmpty()
        {
            // Arrange
            string filePath = "empty.txt";
            File.WriteAllText(filePath, string.Empty);

            // Act & Assert
            try
            {
                Assert.Throws<EmptyFileException>(() => Guard.AgainstEmptyFile(filePath, nameof(filePath)));
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        [Fact]
        public void AgainstInvalidFileExtension_ShouldThrowInvalidFileExtensionException_WhenExtensionIsInvalid()
        {
            // Arrange
            string filePath = "file.invalid";

            // Act & Assert
            Assert.Throws<InvalidFileExtensionException>(() => Guard.AgainstInvalidFileExtension(filePath, new[] { ".txt", ".docx" }, nameof(filePath)));
        }

        [Fact]
        public void AgainstNonAlphanumericCharacters_ShouldThrowOnlyAlphanumericCharacterException_WhenValueContainsSpecialCharacters()
        {
            // Arrange
            string value = "abc123!@#";

            // Act & Assert
            Assert.Throws<OnlyAlphanumericCharacterException>(() => Guard.AgainstNonAlphanumericCharacters(value, nameof(value)));
        }

        [Fact]
        public void AgainstWeakPassword_ShouldThrowWeakPasswordException_WhenPasswordIsWeak()
        {
            // Arrange
            string weakPassword = "password";

            // Act & Assert
            Assert.Throws<WeakPasswordException>(() => Guard.AgainstWeakPassword(weakPassword, nameof(weakPassword)));
        }

        [Fact]
        public void AgainstExceedingCount_ShouldThrowExceedingCountException_WhenCollectionExceedsLimit()
        {
            // Arrange
            var collection = new List<int> { 1, 2, 3, 4, 5 };

            // Act & Assert
            Assert.Throws<ExceedingCountException>(() => Guard.AgainstExceedingCount(collection, 3, nameof(collection)));
        }

        [Fact]
        public void AgainstEmptyCollection_ShouldThrowNullValueException_WhenCollectionIsEmpty()
        {
            // Arrange
            var collection = new List<int>();

            // Act & Assert
            Assert.Throws<NullValueException>(() => Guard.AgainstEmptyCollection(collection, nameof(collection)));
        }

        [Fact]
        public void AgainstInvalidEnum_ShouldThrowInvalidEnumValueException_WhenEnumValueIsInvalid()
        {
            // Arrange
            var invalidEnum = (TestEnum)999;

            // Act & Assert
            Assert.Throws<InvalidEnumValueException>(() => Guard.AgainstInvalidEnum(invalidEnum, nameof(invalidEnum)));
        }

        [Fact]
        public void AgainstInvalidXml_ShouldThrowInvalidXmlException_WhenXmlIsInvalid()
        {
            // Arrange
            string invalidXml = "<invalid>";

            // Act & Assert
            Assert.Throws<InvalidXmlException>(() => Guard.AgainstInvalidXml(invalidXml, nameof(invalidXml)));
        }

        [Fact]
        public void AgainstUnrealisticBirthDate_ShouldThrowUnrealisticBirthDateException_WhenDateIsUnrealistic()
        {
            // Arrange
            DateTime unrealisticDate = DateTime.Now.AddYears(-200);

            // Act & Assert
            Assert.Throws<UnrealisticBirthDateException>(() => Guard.AgainstUnrealisticBirthDate(unrealisticDate, nameof(unrealisticDate)));
        }
    }

    public enum TestEnum
    {
        ValidValue1 = 1,
        ValidValue2 = 2
    }

    public class TestObject
    {
        public string? Name { get; set; }
        public int? Age { get; set; }
    }
}

