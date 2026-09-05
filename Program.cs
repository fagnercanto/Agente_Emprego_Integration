
using AgenteEmprego.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using System;
using System.Net.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        // Resiliência: Se o Notion der 429, o Polly faz o retry automático exponencial.
        services.AddHttpClient<NotionRepositoryService>()
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        // Singleton: Instancia a IA uma única vez na subida do Worker. Zero allocation extra.
        services.AddSingleton<Gemini_Service>();

        // Scoped: Vive apenas durante o ciclo de leitura dos emails.
        services.AddScoped<Gmail_Service>();
    })
    .Build();

host.Run();