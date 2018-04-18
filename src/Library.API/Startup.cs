using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Library.API.Services;
using Library.API.Entities;
using Library.API.Helpers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using NLog.Extensions.Logging;
using StoneCo.Buy4.Infrastructure.Logging;

namespace Library.API
{
    public class Startup
    {
        public static IConfiguration Configuration;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(setupAction =>
            {
                setupAction.ReturnHttpNotAcceptable = true;
                setupAction.OutputFormatters.Add(new XmlDataContractSerializerOutputFormatter());
                setupAction.InputFormatters.Add(new XmlDataContractSerializerInputFormatter());
            });

            // register the DbContext on the container, getting the connection string from
            // appSettings (note: use this during development; in a production environment,
            // it's better to store the connection string in an environment variable)
            var connectionString = Configuration["connectionStrings:libraryDBConnectionString"];
            services.AddDbContext<LibraryContext>(o => o.UseSqlServer(connectionString));

            // register the repository
            services.AddScoped<ILibraryRepository, LibraryRepository>();

            services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
            services.AddScoped<IUrlHelper, UrlHelper>(implementationFactory =>
            {
                var actionContext = implementationFactory.GetService<IActionContextAccessor>().ActionContext;
                return new UrlHelper(actionContext);
            });

            services.AddTransient<IPropertyMappingService, PropertyMappingService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, 
            ILoggerFactory loggerFactory, LibraryContext libraryContext)
        {
            ConfigureStoneCoLog();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler(appBuilder =>
                {
                    appBuilder.Run(async context =>
                    {
                        var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();

                        if (exceptionHandlerFeature != null)
                        {
                            var logger = loggerFactory.CreateLogger("Global exception logger");
                            logger.LogError(500, exceptionHandlerFeature.Error, exceptionHandlerFeature.Error.Message);
                        }

                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync("An unexpected fault happened. Try again later.");
                    });
                });
            }

            Mapper.Initialize(cfg =>
            {
                cfg.CreateMap<Entities.Author, Models.AuthorDto>()
                    .ForMember(dest => dest.Name, opt => opt.MapFrom(src =>
                        $"{src.FirstName} {src.LastName}"))
                    .ForMember(dest => dest.Age, opt => opt.MapFrom(src =>
                        src.DateOfBirth.GetCurrentAge()));

                cfg.CreateMap<Entities.Book, Models.BookDto>();
                cfg.CreateMap<Models.AuthorForCreationDto, Entities.Author>();
                cfg.CreateMap<Models.BookForCreationDto, Entities.Book>();
                cfg.CreateMap<Models.BookForUpdateDto, Entities.Book>();
                cfg.CreateMap<Entities.Book, Models.BookForUpdateDto>();
            });

            libraryContext.EnsureSeedDataForContext();

            app.UseMvc(); 
        }

        private static void ConfigureStoneCoLog()
        {
            LoggerConfiguration configuration = new LoggerConfiguration();

            string serviceEndpoint = Configuration["logger:serviceEndpoint"];
            Enum.TryParse(Configuration["logger:minimumLogSeverity"], out LogSeverity severity);
            Int32.TryParse(Configuration["logger:accumulationPeriodInSeconds"], out int accumulationPeriodInSeconds);
            Int32.TryParse(Configuration["logger:maximumLogEntriesPerRequest "], out int maximumLogEntriesPerRequest);
            Int32.TryParse(Configuration["logger:requestTimeoutInSeconds  "], out int requestTimeoutInSeconds);

            configuration.MinimumLogSeverity = severity;

            if (string.IsNullOrWhiteSpace(serviceEndpoint) == false)
            {
                configuration.ServiceEndpoint = serviceEndpoint;
            }

            if (accumulationPeriodInSeconds > 0)
            {
                configuration.AccumulationPeriodInSeconds = accumulationPeriodInSeconds;
            }

            if (maximumLogEntriesPerRequest > 0)
            {
                configuration.MaximumLogEntriesPerRequest = maximumLogEntriesPerRequest;
            }

            if (requestTimeoutInSeconds > 0)
            {
                configuration.RequestTimeoutInSeconds = requestTimeoutInSeconds;
            }

            Console.WriteLine($"Logando o logger da Stone no seguinte endereço: {configuration.ServiceEndpoint}");

            Logger.LoadConfiguration(configuration);
            Logger.Logging += delegate(IReadOnlyList<LogEntry> list)
            {
                Console.WriteLine("Tentando enviar os logs:");

                foreach (var logEntry in list)
                {
                    Console.WriteLine(logEntry.Message);
                }
            };
            Logger.Logged += delegate(IReadOnlyList<LogEntry> list)
            {
                Console.WriteLine("Logado com sucesso as mensagens:");
                foreach (var logEntry in list)
                {
                    Console.WriteLine(logEntry.Message);
                }
                Logger.Flush();
            };
            Logger.ErrorLogging += delegate(IReadOnlyList<LogEntry> list, Exception exception)
            {
                Console.WriteLine("Erro ao enviar as mensagens:");
                foreach (var logEntry in list)
                {
                    Console.WriteLine(logEntry.Message);
                }
                Console.WriteLine(exception);
            };
        }
    }
}
