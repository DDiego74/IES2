namespace IES_2.Droid
{
    public partial class GraphPage : ContentPage
    {
        public GraphPage(DiagnosticViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
        }
    }
}
