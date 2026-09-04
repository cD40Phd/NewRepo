using EnergyBalance.Application;
using EnergyBalance.DataAccess;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddEnergyBalanceDataAccess(builder.Configuration);
builder.Services.AddEnergyBalanceApplication();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

// Точка расширения для интеграционных тестов (WebApplicationFactory<Program>).
public partial class Program
{
}
