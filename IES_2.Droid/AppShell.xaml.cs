using Microsoft.Extensions.DependencyInjection;

namespace IES_2.Droid
{
    public partial class AppShell : Shell
    {
        public AppShell(IServiceProvider services)
        {
            InitializeComponent();
            // Le pagine vengono risolte dal DI solo quando Shell le richiede,
            // ovvero DOPO che le risorse globali (Colors.xaml, Styles.xaml) sono caricate.
            contentMain.ContentTemplate  = new DataTemplate(() => services.GetRequiredService<MainPage>());
            contentDiag.ContentTemplate  = new DataTemplate(() => services.GetRequiredService<DiagPage>());
            contentGraph.ContentTemplate = new DataTemplate(() => services.GetRequiredService<GraphPage>());
        }
    }
}
