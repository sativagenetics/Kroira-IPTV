#nullable enable
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kroira.App.Data
{
    public sealed class KroiraDesignTimeServices : IDesignTimeServices
    {
        public void ConfigureDesignTimeServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton<IMigrationsIdGenerator, MonotonicMigrationsIdGenerator>());
        }
    }
}
