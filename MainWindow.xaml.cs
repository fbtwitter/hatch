using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace TodoWinUI3;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "To-Do";
        this.AppWindow.Resize(new SizeInt32(520, 800));
    }
}
