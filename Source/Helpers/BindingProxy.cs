using System.Windows;

namespace TraceTiger.Helpers;

/// <summary>Freezable proxy so DataTemplates (context menus, item cards) can bind to the
/// parent view-model's commands via <c>{Binding Data.SomeCommand, Source={StaticResource Proxy}}</c>.</summary>
public class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
