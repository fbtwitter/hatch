using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TodoWinUI3.Models;
using TodoWinUI3.ViewModels;
using Windows.System;

namespace TodoWinUI3.Views;

public sealed partial class MainPage : Page
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainPage()
    {
        this.InitializeComponent();
        this.DataContext = new MainViewModel();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddTaskCommand.CanExecute(null))
            ViewModel.AddTaskCommand.Execute(null);
    }

    private void CheckBox_Checked(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((CheckBox)sender).Tag;
        ViewModel.SetTaskCompleted(task, true);
    }

    private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((CheckBox)sender).Tag;
        ViewModel.SetTaskCompleted(task, false);
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((Button)sender).Tag;

        var editBox = new TextBox
        {
            Text = task.Title,
            MinWidth = 300,
            PlaceholderText = "Task title"
        };

        var dialog = new ContentDialog
        {
            Title = "Edit Task",
            Content = editBox,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(editBox.Text))
            ViewModel.UpdateTaskTitle(task, editBox.Text.Trim());
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((Button)sender).Tag;
        ViewModel.DeleteTask(task);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SettingsPage));
    }
}
