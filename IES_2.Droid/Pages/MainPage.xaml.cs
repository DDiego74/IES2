using Plugin.BLE.Abstractions.Contracts;

namespace IES_2.Droid
{
    public partial class MainPage : ContentPage
    {
        private readonly DiagnosticViewModel _vm;

        public MainPage(DiagnosticViewModel vm)
        {
            InitializeComponent();
            _vm            = vm;
            BindingContext = vm;
            Resources.Add("ScanBtnTextConverter",  new ScanBtnTextConverter());
            Resources.Add("InvertBoolConverter",   new InvertBoolConverter());
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _vm.RequestBlePermissionsAsync();
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is IDevice device)
                await _vm.ConnectAsync(device);
        }
    }
}
