using AMR.DeliveryPlanning.Api.Middlewares;
using AMR.DeliveryPlanning.Api.Modules;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure MediatR — scan all module Application assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Facility.Application.Class1).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Fleet.Application.Class1).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip.DispatchTripCommand).Assembly);
});

// Register all module services (DbContexts, Repositories, Domain Services, HttpClients)
builder.Services.AddAllModules(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

// Map all module Minimal API endpoints
app.MapAllModuleEndpoints();

app.Run();
