using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AIHub.ViewModels;

namespace AIHub.Views
{
    public partial class RegisterPage : UserControl
    {
        public RegisterPage()
        {
            InitializeComponent();
            Loaded += RegisterPage_Loaded;
        }

        private void RegisterPage_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
            }

            RegisterPasswordBox.PasswordChanged += RegisterPasswordBox_PasswordChanged;
            ConfirmPasswordBox.PasswordChanged += ConfirmPasswordBox_PasswordChanged;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is RegisterViewModel vm && e.PropertyName == nameof(RegisterViewModel.ShowPassword) && !vm.ShowPassword)
            {
                RegisterPasswordBox.Password = vm.Password;
                ConfirmPasswordBox.Password = vm.ConfirmPassword;
            }
        }

        private void RegisterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterViewModel vm)
            {
                vm.Password = RegisterPasswordBox.Password;
            }
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterViewModel vm)
            {
                vm.ConfirmPassword = ConfirmPasswordBox.Password;
            }
        }
    }
}
