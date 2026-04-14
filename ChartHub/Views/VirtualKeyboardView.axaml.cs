using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using ChartHub.ViewModels;

#if ANDROID
using Android.Content;
using Android.Views.InputMethods;
#endif

namespace ChartHub.Views;

public partial class VirtualKeyboardView : UserControl
{
    public VirtualKeyboardView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VirtualKeyboardViewModel vm)
            {
                vm.Activate();
            }

            FocusCaptureBox();
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is VirtualKeyboardViewModel vm)
            {
                vm.Deactivate();
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void FocusCaptureBox()
    {
        TextBox? captureBox = this.FindControl<TextBox>("CaptureBox");
        captureBox?.Focus();

#if ANDROID
        MainActivity? activity = ChartHub.MainActivity.Current;
        if (activity != null && captureBox != null)
        {
            var imm = (InputMethodManager?)activity.GetSystemService(Context.InputMethodService);
            imm?.ShowSoftInput(activity.Window?.DecorView, ShowFlags.Implicit);
        }
#endif
    }
}
