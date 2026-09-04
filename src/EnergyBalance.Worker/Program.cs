using EnergyBalance.Acquisition;
using EnergyBalance.Application;
using EnergyBalance.DataAccess;
using EnergyBalance.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddEnergyBalanceDataAccess(builder.Configuration);
builder.Services.AddEnergyBalanceAcquisition(builder.Configuration);
builder.Services.AddEnergyBalanceApplication();

builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddHostedService<BalanceCalculationWorker>();

var host = builder.Build();
host.Run();
