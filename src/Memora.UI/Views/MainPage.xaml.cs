using DevCache.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DevCache.UI.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = new();

        public MainPage()
        {
            InitializeComponent();
            DataContext = ViewModel;
        }

        private void SetKeyButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SetKeyTeachingTip.IsOpen = true;
        }

        private void SetKeyTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            SetKeyTeachingTip.IsOpen = false;
        }

        private void DeleteKeyButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            DeleteKeyTeachingTip.IsOpen = true;
        }

        private void DeleteKeyTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            DeleteKeyTeachingTip.IsOpen = false;
        }

        private void ExpireKeyButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenExpireTipCommand.Execute(null);
            ExpireKeyTeachingTip.IsOpen = true;
        }

        private async void ExpireKeyTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            await ViewModel.ExpireKeyConfirmCommand.ExecuteAsync(null);
            sender.IsOpen = false;  // close after apply
        }

        private void ExpireKeyTeachingTip_Closed(TeachingTip sender, object args)
        {
            // Optional cleanup
            ViewModel.ExpireSeconds = 3600; // reset default
        }

        private void KeyGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            //ShowKeyTeachingTip.IsOpen = true;
        }

        private void FlushAllButton_Click(object sender, RoutedEventArgs e)
        {
            FlushAllTeachingTip.IsOpen = true;
        }

        private void FlushAllTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            FlushAllTeachingTip.IsOpen = false;
        }

        private void ClosePane_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
