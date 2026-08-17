using System.Windows;
using System.Windows.Controls;

namespace UDM18.Client.Behaviors;

public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordBoxAssistant), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed));
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(PasswordBoxAssistant));
    public static string GetBoundPassword(DependencyObject value) => (string?)value.GetValue(BoundPasswordProperty) ?? string.Empty;
    public static void SetBoundPassword(DependencyObject value, string password) => value.SetValue(BoundPasswordProperty, password);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not PasswordBox box) return;
        box.PasswordChanged -= PasswordChanged;
        if (!(bool)box.GetValue(UpdatingProperty)) box.Password = args.NewValue as string ?? string.Empty;
        box.PasswordChanged += PasswordChanged;
    }
    private static void PasswordChanged(object sender, RoutedEventArgs args)
    {
        var box = (PasswordBox)sender;
        box.SetValue(UpdatingProperty, true);
        UpdateBindingSource(box, box.Password);
        box.SetValue(UpdatingProperty, false);
    }

    private static void UpdateBindingSource(PasswordBox box, string password)
    {
        var expression = box.GetBindingExpression(BoundPasswordProperty);
        var source = expression?.DataItem;
        var path = expression?.ParentBinding.Path?.Path;
        if (source is null || string.IsNullOrWhiteSpace(path)) return;

        var segments = path.Split('.');
        object? owner = source;
        for (var index = 0; index < segments.Length - 1 && owner is not null; index++)
            owner = owner.GetType().GetProperty(segments[index])?.GetValue(owner);

        var property = owner?.GetType().GetProperty(segments[^1]);
        if (property?.CanWrite == true) property.SetValue(owner, password);
    }
}
