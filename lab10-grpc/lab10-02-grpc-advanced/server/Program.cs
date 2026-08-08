using Microsoft.AspNetCore.Server.Kestrel.Core;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Listen on :50051 with plain-text HTTP/2 (same as the Go gRPC server)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(50051, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection(); // required for gRPCUI / grpcurl

var app = builder.Build();

app.MapGrpcService<ProductServiceImpl>();
app.MapGrpcReflectionService();

Console.WriteLine("gRPC server with streaming on :50051");
app.Run();
