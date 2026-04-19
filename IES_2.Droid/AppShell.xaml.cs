namespace IES_2.Droid
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage, DiagPage diagPage, GraphPage graphPage)
        {
            InitializeComponent();
            // Shell uses ContentTemplate's factory for lazy page rendering.
            // Assigning Content directly bypasses Shell's lazy-loader so the
            // page never gets rendered (transparent/empty window).
            // Wrapping each DI-created page in a DataTemplate factory fixes this.
            contentMain.ContentTemplate  = new DataTemplate(() => mainPage);
            contentDiag.ContentTemplate  = new DataTemplate(() => diagPage);
            contentGraph.ContentTemplate = new DataTemplate(() => graphPage);
        }
    }
}
