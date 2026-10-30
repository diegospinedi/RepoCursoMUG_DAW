using Optica.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AgregarInfraestructura(builder.Configuration);

var app = builder.Build();

// Aplica las migraciones pendientes. Si el directorio de datos no se puede crear o escribir, registra
// el error con la ruta y la clave Optica:Datos:Directorio, y la Api no arranca.
app.Services.InicializarBaseDeDatos();

app.Run();
