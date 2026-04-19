namespace IES_2.Droid
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage, DiagPage diagPage, GraphPage graphPage)
        {
            InitializeComponent();
            // Assign DI-resolved pages directly so their constructor injection works.
            contentMain.Content  = mainPage;
            contentDiag.Content  = diagPage;
            contentGraph.Content = graphPage;
        }
    }
}
