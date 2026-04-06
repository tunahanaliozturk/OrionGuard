using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Blazor;

/// <summary>
/// Blazor component that validates EditForm models using registered IValidator implementations.
/// Usage: &lt;OrionGuardFluentValidator TModel="MyModel" /&gt; inside an EditForm.
/// </summary>
public sealed class OrionGuardFluentValidator<TModel> : ComponentBase, IDisposable where TModel : class
{
    private EditContext? _editContext;
    private ValidationMessageStore? _messageStore;

    [CascadingParameter]
    private EditContext? CurrentEditContext { get; set; }

    [Inject]
    private IValidator<TModel>? Validator { get; set; }

    protected override void OnInitialized()
    {
        if (CurrentEditContext is null)
            throw new InvalidOperationException("OrionGuardFluentValidator requires a cascading parameter of type EditContext.");

        _editContext = CurrentEditContext;
        _messageStore = new ValidationMessageStore(_editContext);

        _editContext.OnValidationRequested += HandleValidationRequested;
        _editContext.OnFieldChanged += HandleFieldChanged;
    }

    private void HandleValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _messageStore?.Clear();

        if (Validator is null || _editContext?.Model is not TModel model) return;

        var result = Validator.Validate(model);
        ApplyErrors(result);
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _messageStore?.Clear(e.FieldIdentifier);

        if (Validator is null || _editContext?.Model is not TModel model) return;

        var result = Validator.Validate(model);
        ApplyErrors(result, e.FieldIdentifier.FieldName);
    }

    private void ApplyErrors(GuardResult result, string? fieldName = null)
    {
        if (result.IsInvalid)
        {
            foreach (var error in result.Errors)
            {
                if (fieldName is not null && error.ParameterName != fieldName)
                    continue;

                var fieldIdentifier = new FieldIdentifier(_editContext!.Model, error.ParameterName);
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
