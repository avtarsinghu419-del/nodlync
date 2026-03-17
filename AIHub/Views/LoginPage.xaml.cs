using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AIHub.ViewModels;

namespace AIHub.Views
{
    public partial class LoginPage : UserControl
    {
        public LoginPage()
        {
            InitializeComponent();
            Loaded += LoginPage_Loaded;
        }

        private void LoginPage_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
            }

            PasswordBox.PasswordChanged += PasswordBox_PasswordChanged;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is LoginViewModel vm && e.PropertyName == nameof(LoginViewModel.ShowPassword) && !vm.ShowPassword)
            {
                // Keep the PasswordBox synced when switching back to masked mode
                PasswordBox.Password = vm.Password;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }
    }
}
