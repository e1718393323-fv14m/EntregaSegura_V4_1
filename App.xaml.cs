namespace EntregaSegura
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent(); // Inicializar el sistema con fondo blanco
            UserAppTheme = AppTheme.Light;
            Services.OfflineService.Iniciar();   // escucha los cambios de conexión para sincronizar solo
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // La app inicia en el login y navega con Navigation.PushAsync
            return new Window(new NavigationPage(new Views.vLogin())
            {
                BarBackgroundColor = Color.FromArgb("#0B1F3A"),
                BarTextColor = Colors.White
            });
        }
    }
}
