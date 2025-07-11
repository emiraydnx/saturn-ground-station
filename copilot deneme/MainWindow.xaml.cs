using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace copilot_deneme
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // MainWindow.xaml.cs veya App.xaml.cs i�inde
        public MainWindow()
        {
            this.InitializeComponent();
            DispatcherQueue.TryEnqueue(() =>
            {
                ContentFrame.Navigate(typeof(AccountPage)); // ContentFrame ana Frame'iniz ise
            });
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                switch (selectedItem.Tag)
                {
                    case "home":
                        // E�er kullan�c� giri� yapmad�ysa HomePage'e y�nlendirme!
                        if (AuthService.IsSignedIn)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ContentFrame.Navigate(typeof(HomePage));
                            });
                        }
                        else
                        {
                            // �sterseniz kullan�c�ya uyar� g�sterebilirsiniz
                            // veya AccountPage'e y�nlendirebilirsiniz
                        }
                        break;
                    
                    case "profile":
                        if (AuthService.IsSignedIn)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ContentFrame.Navigate(typeof(ChartPage)); // ChatPage yerine ChartPage kulland�m ��nk� ChatPage yoktu
                            });
                        }
                        break;
                    
                    case "settings":
                        if (AuthService.IsSignedIn)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ContentFrame.Navigate(typeof(SettingPage));
                            });
                        }
                        break;
                    
                    case "test":
                        if (AuthService.IsSignedIn)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ContentFrame.Navigate(typeof(TestPage));
                            });
                        }
                        break;
                        // Di�erleri...
                }
            }
        }
    }
}
