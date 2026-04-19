namespace IES_2.Droid
{
    public partial class App : Application
    {
        private readonly AppShell _shell;

        // AppShell is resolved from DI (registered in MauiProgram) so that
        // all page constructors receive their injected dependencies.
        public App(AppShell shell)
        {
            InitializeComponent();
            _shell = shell;
        }

        protected override Window CreateWindow(IActivationState activationState)
            => new Window(_shell);
    }
}
