using System.Windows;
using System.Windows.Controls;

namespace DiskScout.Views.Controls;

public partial class EmptyStatePanel : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(EmptyStatePanel),
            new PropertyMetadata(string.Empty));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public EmptyStatePanel()
    {
        InitializeComponent();
    }
}
