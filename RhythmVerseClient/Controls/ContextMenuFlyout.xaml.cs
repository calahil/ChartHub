using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Controls
{
    public partial class ContextMenuFlyout : ContentView
    {
        public enum MenuCommand
        {
            Delete,
            Open,
            Extract,
            Preview
        }
        public static readonly BindableProperty MenuCommandsProperty =
           BindableProperty.Create(nameof(MenuCommands), typeof(MenuCommand[]), typeof(ContextMenuFlyout), null, propertyChanged: OnMenuCommandsChanged);

        public static readonly BindableProperty SelectedFileProperty =
            BindableProperty.Create(nameof(SelectedFile), typeof(FileData), typeof(ContextMenuFlyout), null, propertyChanged: OnSelectedFileChanged);

        public MenuCommand[] MenuCommands
        {
            get => (MenuCommand[])GetValue(MenuCommandsProperty);
            set => SetValue(MenuCommandsProperty, value);
        }

        public FileData SelectedFile
        {
            get => (FileData)GetValue(SelectedFileProperty);
            set => SetValue(SelectedFileProperty, value);
        }

        public ObservableCollection<MenuFlyoutItem> ContextMenuItems { get; set; }

        public ContextMenuFlyout()
        {
            InitializeComponent();
            BindingContext = this;
            ContextMenuItems = new ObservableCollection<MenuFlyoutItem>();
            GenerateContextMenuItems();
        }

        private static void OnMenuCommandsChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is ContextMenuFlyout control)
            {
                control.GenerateContextMenuItems();
            }
        }

        private static void OnSelectedFileChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is ContextMenuFlyout control)
            {
                control.UpdateMenuItemsState();
            }
        }

        public void GenerateContextMenuItems()
        {
            ContextMenuItems.Clear();

            if (MenuCommands == null) return;

            foreach (var menuCommand in MenuCommands)
            {
                var item = new MenuFlyoutItem
                {
                    Text = menuCommand.ToString(),
                    Command = GetCommand(menuCommand),
                    CommandParameter = SelectedFile
                };
                ContextMenuItems.Add(item);
            }

            UpdateMenuItemsState();
        }

        private void UpdateMenuItemsState()
        {
            foreach (var item in ContextMenuItems)
            {
                if (item.Command is Command command)
                {
                    command.ChangeCanExecute();
                }
            }
        }

        private ICommand GetCommand(MenuCommand menuCommand)
        {
            switch (menuCommand)
            {
                case MenuCommand.Delete:
                    return ((DownloadViewModel)BindingContext).DeleteCommand;
                case MenuCommand.Open:
                    return ((DownloadViewModel)BindingContext).OpenCommand;
                case MenuCommand.Extract:
                    return ((DownloadViewModel)BindingContext).ExtractCommand;
                case MenuCommand.Preview:
                    return ((DownloadViewModel)BindingContext).PreviewCommand;
                default:
                    return null;
            }
        }
    }
}