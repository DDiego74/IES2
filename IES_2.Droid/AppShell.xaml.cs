namespace IES_2.Droid
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage, DiagPage diagPage, GraphPage graphPage)
        {
            InitializeComponent();
            // Assign DI-resolved pages directly so their constructor injection works.
            System.Diagnostics.Debug.WriteLine("AppShell constructor called");
            contentMain.Content  = mainPage;
            contentDiag.Content  = diagPage;
            contentGraph.Content = graphPage;
        }
    }
}
