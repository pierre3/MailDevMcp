using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
builder.Services.AddHttpClient("MailDev", client =>
{
    var port = Environment.GetEnvironmentVariable("MAILDEV_API_PORT") ?? "1080";
    client.BaseAddress = new Uri($"http://localhost:{port}");
});
await builder.Build().RunAsync();
