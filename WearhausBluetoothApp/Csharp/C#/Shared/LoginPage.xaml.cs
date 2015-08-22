using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using WearhausHttp;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class LoginPage : Page
    {
        WearhausHttpController HttpController;

        public LoginPage(WearhausHttpController httpController)
        {
            this.InitializeComponent();

            HttpController = httpController;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string resp = await HttpController.VerifyCredentials(EmailTextBox.Text, PasswordTextBox.Text);
        }

        private void SignupButton_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}
