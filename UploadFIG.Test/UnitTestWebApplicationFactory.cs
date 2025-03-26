extern alias r4b;

using Hl7.Fhir.DemoFileSystemFhirServer;
using Hl7.Fhir.NetCoreApi;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;

namespace UnitTestWebApi
{
    internal class UnitTestFhirServerApplication : WebApplicationFactory<Startup>
    {
        private readonly string _environment;

        public UnitTestFhirServerApplication(string environment = "Development")
        {
            _environment = environment;
            Server.BaseAddress = new Uri("https://localhost/");
        }

        protected override IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(x => x.UseStartup<Startup>()
                    .UseTestServer());
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            return base.CreateHost(builder);
        }
    }

    public class Startup
    {
        public Startup(IWebHostEnvironment env)
        {
            _env = env;
        }
        private IWebHostEnvironment _env;

        public required IConfiguration Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // Load the configuration settings
            var configBuilder = new ConfigurationBuilder()
               .SetBasePath(_env.ContentRootPath)
               .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
               .AddEnvironmentVariables();
            Configuration = configBuilder.Build();

            services.AddLogging(logging => {
                logging.AddConsole(config => {
                    //  config.LogToStandardErrorThreshold = LogLevel.Trace;
                });
            });

            string tempFIGPath = Path.Combine(Path.GetTempPath(), "UploadFIG");
            string unitTestPath = Path.Combine(tempFIGPath, "unit-test-data");
            if (!System.IO.Directory.Exists(unitTestPath))
                System.IO.Directory.CreateDirectory(unitTestPath);
            else
            {
                // Clean out the directory
                foreach (var file in System.IO.Directory.GetFiles(unitTestPath))
                    System.IO.File.Delete(file);
            }

            DirectorySystemService<System.IServiceProvider>.Directory = unitTestPath;
            if (!System.IO.Directory.Exists(DirectorySystemService<System.IServiceProvider>.Directory))
                System.IO.Directory.CreateDirectory(DirectorySystemService<System.IServiceProvider>.Directory);

            services.AddSingleton<IFhirSystemServiceR4<IServiceProvider>>((s) => {
                var systemService = new DirectorySystemService<System.IServiceProvider>();
                systemService.InitializeIndexes();
                return systemService;
            });

            services.UseFhirServerController();

            // Tell the Net stack to only use TLS
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            // This should not be in any production system, it
            // essentially permits dud certs being used
            // unchecked.
            System.Net.ServicePointManager.ServerCertificateValidationCallback += (sender2, cert2, chain, sslPolicyErrors) => true;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseDeveloperExceptionPage();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }

}
