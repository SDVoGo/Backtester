using Microsoft.Extensions.DependencyInjection;

namespace Backtester.Infrastructure.Extensions
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddBacktester(this IServiceCollection services)
        {
            services.AddScoped<Backtester.Engine.Engine>();

            // Repository
            // Data provider
            // Altri servizi

            return services;
        }
    }
}
