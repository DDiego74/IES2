namespace IES_2.Droid
{
    public partial class DiagPage : ContentPage
    {
        public DiagPage(DiagnosticViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
        }
    }
}
