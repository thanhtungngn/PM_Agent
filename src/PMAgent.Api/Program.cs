using PMAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title       = "PM Agent API",
        Version     = "v1",
        Description = "Orchestrator agentic system — PO · PM · BA · DEV · TEST"
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PM Agent v1");
    options.RoutePrefix = string.Empty; // Swagger UI at root: http://localhost:<port>/
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
