using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Blazor;

/// <summary>
/// Blazor component that validates EditForm models using OrionGuard validation attributes.
/// Replaces DataAnnotationsValidator with OrionGuard's validation system.
/// Usage: &lt;OrionGuardValidator /&gt; inside an EditForm.
/// </summary>
public sealed class OrionGuardValidator : ComponentBase, IDisposable
{
    private EditContext? _editContext;
    private ValidationMessageStore? _messageStore;

    [CascadingParameter]
    private EditContext? CurrentEditContext { get; set; }

    protected override void OnInitialized()
    {
        if (CurrentEditContext is null)
            throw new InvalidOperationException("OrionGuardValidator requires a cascading parameter of type EditContext. Use it inside an EditForm.");

        _editContext = CurrentEditContext;
        _messageStore = new ValidationMessageStore(_editContext);

        _editContext.OnValidationRequested += HandleValidationRequested;
        _editContext.OnFieldChanged += HandleFieldChanged;
    }

    private void HandleValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _messageStore?.Clear();
        var model = _editContext!.Model;
        ValidateModel(model);
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _messageStore?.Clear(e.FieldIdentifier);
        var model = _editContext!.Model;
        ValidateModel(model, e.FieldIdentifier.FieldName);
    }

    private void ValidateModel(object model, string? fieldName = null)
    {
        // Use reflection to call AttributeValidator.Validate<T> with the correct type
        var validateMethod = typeof(AttributeValidator)
            .GetMethod(nameof(AttributeValidator.Validate))!
            .MakeGenericMethod(model.GetType());

        var result = (GuardResult)validateMethod.Invoke(null, new[] { model })!;

        if (result.IsInvalid)
        {
            foreach (var error in result.Errors)
            {
                if (fieldName is not null && error.ParameterName != fieldName)
                    continue;

                var fieldIdentifier = new FieldIdentifier(model, error.ParameterName);
                _messageStore?.Add(fieldIdentifier, error.Message);
            }
        }

        _editContext?.NotifyValidationStateChanged();
    }

    public void Dispose()
    {
        if (_editContext is not null)
        {
            _editContext.OnValidationRequested -= HandleValidationRequested;
            _editContext.OnFieldChanged -= HandleFieldChanged;
        }
    }
}
