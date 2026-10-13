using Hatch.Models;
using Hatch.ViewModels;
using System.Collections.ObjectModel;

// Portable Core models cross the WinUI ABI without taking a Windows dependency.
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(TodoItem))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(Step))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(TaskList))]

// Register concrete collections crossing WinRT; interface-typed bindings
// and collection expressions are not reliably discovered by the WinRT AOT generator.
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(List<CompletedTaskGroup>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(List<PlannedGroup>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(List<Step>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(List<string>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(string[]))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(List<Windows.UI.Color>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<TodoItem>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<TaskList>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<StatTileInfo>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<UpcomingTaskInfo>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<RhythmBarInfo>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<MascotOpenPageOption>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<string>))]
