using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Hatch.Models;
using Hatch.ViewModels;
using Windows.System;

namespace Hatch.Views;

public sealed partial class TaskListPage : Page
{
    private MainViewModel? _vm;
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public TaskListPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MainViewModel vm) return;

        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = vm;
        DataContext = vm;
        UpdateView(vm.ActiveNavItem);
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_vm == null) return;
        if (args.PropertyName == nameof(MainViewModel.ActiveNavItem))
            UpdateView(_vm.ActiveNavItem);
        else if (args.PropertyName == nameof(MainViewModel.PlannedGroups) && _vm.ActiveNavItem == "planned")
            RefreshPlannedGroups();
    }

    private void UpdateView(string navItem)
    {
        HeaderText.Text = navItem switch
        {
            "myday"     => "My Day",
            "important" => "Important",
            "planned"   => "Planned",
            _           => "All Tasks"
        };

        var isPlanned = navItem == "planned";
        FlatListView.Visibility    = isPlanned ? Visibility.Collapsed : Visibility.Visible;
        GroupedListView.Visibility = isPlanned ? Visibility.Visible   : Visibility.Collapsed;

        if (isPlanned)
            RefreshPlannedGroups();
    }

    private void RefreshPlannedGroups()
    {
        if (_vm == null) return;
        var cvs = (CollectionViewSource)Resources["PlannedGroupsSource"];
        cvs.Source = _vm.PlannedGroups;
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddTaskCommand.CanExecute(null))
            ViewModel.AddTaskCommand.Execute(null);
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

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((Button)sender).Tag;
        if (task is null) return;
        task.IsStarred = !task.IsStarred;
    }
}
