using System.Windows;

namespace Sortify.Views;

/// <summary>
/// Carries a binding to places outside the visual tree that can't reach the DataContext,
/// such as a DataGrid column's Header or Visibility. Declared as a resource with
/// <c>Data="{Binding}"</c>; as a Freezable in a resource dictionary it inherits the owning
/// element's DataContext, which a column can then bind to through
/// <c>Source={StaticResource ...}</c>.
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
